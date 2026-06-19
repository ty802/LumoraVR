// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Networking.Streams;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Head output device types.
/// </summary>
public enum HeadOutputDevice
{
    Server,
    Screen,
    VR,
    Camera
}

/// <summary>
/// Platform types.
/// </summary>
public enum Platform
{
    Windows,
    Linux,
    Android,
    Other
}

/// <summary>
/// Represents a user in the world.
/// </summary>
public class User : ContainerWorker<UserComponent>, ISyncObject, IDisposable
{
    private List<ISyncMember> _syncMembers = new();
    private readonly Dictionary<BodyNode, TrackingStreamPair> _trackingStreams = new();
    private bool _trackingStreamsInitialized;
    private readonly HashSet<IStream> _justAddedStreams = new();

    private const string TrackingGroupName = "Tracking";
    private static readonly BodyNode[] TrackingNodes =
    {
        BodyNode.Head,
        BodyNode.LeftController,
        BodyNode.RightController
    };

    private struct TrackingStreamPair
    {
        public Float3ValueStream Position;
        public FloatQValueStream Rotation;

        public TrackingStreamPair(Float3ValueStream position, FloatQValueStream rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public readonly Sync<string> UserName = new();
    public readonly Sync<string> UserID = new();
    public readonly Sync<string> MachineID = new();
    public readonly Sync<ulong> AllocationIDStart = new();
    public readonly Sync<ulong> AllocationIDEnd = new();
    public readonly Sync<byte> AllocationID = new();
    public readonly Sync<int> Ping = new();
    public readonly Sync<bool> IsPresent = new();
    public readonly Sync<bool> IsSilenced = new();
    public readonly Sync<bool> IsMuted = new();
    public readonly Sync<bool> VRActive = new();
    public readonly Sync<bool> PresentInWorld = new();
    public readonly Sync<bool> PresentInHeadset = new();
    public readonly Sync<bool> EditMode = new();
    public readonly Sync<float> FPS = new();
    public readonly Sync<HeadOutputDevice> HeadDevice = new();
    public readonly Sync<Platform> UserPlatform = new();
    public readonly Sync<float> DownloadSpeed = new();
    public readonly Sync<float> UploadSpeed = new();
    public readonly Sync<ulong> DownloadedBytes = new();
    public readonly Sync<ulong> UploadedBytes = new();
    public readonly UserStreamStorage userStreams = new();
    public readonly Sync<uint> streamConfiguration = new();

    // Network statistics (non-synced)
    public ulong SentBytes { get; set; }
    public ulong ReceivedBytes { get; set; }
    public DateTime LastSyncMessage { get; set; }
    public int ImmediateDeltaCount { get; set; }
    public int ImmediateControlCount { get; set; }
    public int ImmediateStreamCount { get; set; }

    // ISyncObject implementation
    public new List<ISyncMember> SyncMembers => _syncMembers;
    public bool IsAuthority => World?.IsAuthority ?? false;

    /// <summary>
    /// Hierarchy path for debugging.
    /// </summary>
    public override string ParentHierarchyToString() => $"User:{UserName.Value ?? UserID.Value}";

    public bool IsHost => World?.IsAuthority == true && World.LocalUser == this;
    public bool IsDisposed { get; private set; }

    public void Destroy()
    {
        Dispose();
    }

    public bool ReceiveStreams { get; set; } = true;

    public Lumora.Core.Networking.Sync.UserStreamBag LegacyStreamBag { get; private set; } = new();

    public IEnumerable<Stream> Streams => userStreams.Streams;

    public int StreamCount => userStreams.Count;

    public StreamGroupManager StreamGroupManager { get; private set; } = null!;

    public uint StreamConfigurationVersion => streamConfiguration.Value;

    /// <summary>
    /// Whether this is the local user.
    /// </summary>
    public bool IsLocal => World?.LocalUser == this;

    private Components.UserRoot _root = null!;
    public readonly SyncRef<Components.UserRoot> UserRootRef = new();

    /// <summary>
    /// UserRoot component for this user.
    /// </summary>
    public Components.UserRoot Root
    {
        get => _root;
        set
        {
            if (World?.IsAuthority == true || World?.LocalUser == this)
            {
                _root = value;
                UserRootRef.Target = value;
                if (value != null)
                {
                    var scope = World?.LocalUser == this ? "local" : "authority";
                    LumoraLogger.Log($"User: Registered UserRoot for {scope} user '{UserName.Value}'");
                }
            }
            else
            {
                throw new Exception("Only the user or authority can register their UserRoot");
            }
        }
    }

    public User()
    {
    }

    internal void InitializeFromCollection(World world, RefID refID)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        var refController = world.ReferenceController;
        if (refController == null)
            throw new InvalidOperationException("ReferenceController required to initialize User.");

        IsInInitPhase = true;
        refController.AllocationBlockBegin(refID);
        try
        {
            base.Initialize(world, parent: null);

            // Identity and namespace fields are the host's to author, full stop. Without this a guest
            // can overwrite its own UserID/MachineID (which is how bans are keyed, so that's a ban-dodge)
            // or rewrite its AllocationID to claim another user's RefID namespace and then author their
            // objects. MarkHostOnly makes Validate reject any guest write to these on the authority; the
            // host still authors them locally just fine. -xlinka
            UserName.MarkHostOnly();
            UserID.MarkHostOnly();
            MachineID.MarkHostOnly();
            AllocationID.MarkHostOnly();
            AllocationIDStart.MarkHostOnly();
            AllocationIDEnd.MarkHostOnly();

            EndInitializationStageForMembers();

            _syncMembers = new List<ISyncMember>(SyncMemberCount);
            for (int i = 0; i < SyncMemberCount; i++)
            {
                _syncMembers.Add(GetSyncMember(i));
            }

            StreamGroupManager = new StreamGroupManager(this);
            userStreams.Initialize(this);
            userStreams.OnElementAdded += OnStreamAdded;
            userStreams.OnElementRemoved += OnStreamRemoved;

            // Bind UserRootRef changes to keep UserRoot.ActiveUser in sync
            UserRootRef.OnTargetChange += OnUserRootRefChanged;

            // Only create tracking streams on authority - clients receive them via sync
            if (world.IsAuthority)
            {
                EnsureTrackingStreamsInitialized();
            }
            EndInitPhase();

            LumoraLogger.Debug($"User created with {_syncMembers.Count} sync members");
        }
        finally
        {
            refController.AllocationBlockEnd();
        }
    }

    /// <summary>
    /// Called when User is fully initialized (after network sync).
    /// Detects if this is the local user based on Session.LocalUserRefIDToInit.
    /// Uses Initialize() to handle local user detection.
    /// </summary>
    internal void Initialize()
    {
        // Only clients need to detect their local user this way
        // Authority sets local user directly via CreateHostUser
        if (!World.IsAuthority && World.LocalUser == null)
        {
            var session = World.Session;
            var syncManager = session?.Sync;
            // Guard against a null/default target matching a user whose RefID also defaulted to 0, so a
            // malformed JoinGrant can't trick a client into adopting the wrong user as local. -xlinka
            if (syncManager != null && !syncManager.LocalUserRefIDToInit.IsNull && ReferenceID == syncManager.LocalUserRefIDToInit)
            {
                World.SetLocalUser(this);
                LumoraLogger.Log($"User.Initialize: Set local user '{UserName.Value}' (RefID: {ReferenceID})");
            }
        }
    }

    /// <summary>
    /// Set user name (authority or local user only).
    /// </summary>
    public void SetUserName(string name)
    {
        if (IsAuthority || World.LocalUser == this)
        {
            UserName.Value = name;
        }
        else
        {
            LumoraLogger.Warn("Non-authority user cannot change username");
        }
    }

    /// <summary>
    /// Kick this user from the world. Host-authoritative; cannot kick the host.
    /// </summary>
    public void Kick()
    {
        if (World == null || !World.IsAuthority)
        {
            LumoraLogger.Warn("Kick requires host authority");
            return;
        }
        if (IsHost)
        {
            LumoraLogger.Warn("Cannot kick the host");
            return;
        }
        LumoraLogger.Log($"Kicking user '{UserName.Value}'");
        World.RemoveUser(this);
    }

    /// <summary>
    /// Ban this user: record a temp + persistent ban (keyed by user/machine id, scoped to this world)
    /// so they can't rejoin, then kick. Host-authoritative; cannot ban the host.
    /// </summary>
    public void Ban()
    {
        if (World == null || !World.IsAuthority)
        {
            LumoraLogger.Warn("Ban requires host authority");
            return;
        }
        if (IsHost)
        {
            LumoraLogger.Warn("Cannot ban the host");
            return;
        }
        LumoraLogger.Log($"Banning user '{UserName.Value}'");
        Security.BanManager.TempBan(UserID.Value, MachineID.Value);
        Security.BanManager.AddBan(UserName.Value, UserID.Value, MachineID.Value, World.WorldName.Value);
        World.RemoveUser(this);
    }

    /// <summary>
    /// Get all dirty sync members that need to be sent over network.
    /// </summary>
    public List<ISyncMember> GetDirtySyncMembers()
    {
        var dirty = new List<ISyncMember>();
        foreach (var member in _syncMembers)
        {
            if (member.IsDirty)
            {
                dirty.Add(member);
            }
        }
        return dirty;
    }

    /// <summary>
    /// Clear dirty flags after sync.
    /// </summary>
    public void ClearDirtyFlags()
    {
        foreach (var member in _syncMembers)
        {
            member.IsDirty = false;
        }
    }

    #region Stream Management

    public IStream GetStream(RefID id)
    {
        return (userStreams.TryGetValue(id, out var stream) ? stream : null) ?? null!;
    }

    public S AddStream<S>() where S : Networking.Streams.Stream, new()
    {
        var stream = new S();
        RefID key = World.ReferenceController.PeekID();
        userStreams.Add(key, stream, isNewlyCreated: true);
        return stream;
    }

    public void RemoveStream(Networking.Streams.Stream stream)
    {
        userStreams.Remove(stream.ReferenceID);
    }

    public void StreamConfigurationChanged()
    {
        if (!streamConfiguration.IsSyncDirty)
        {
            streamConfiguration.Value++;
        }
    }

    public bool WasStreamJustAdded(Networking.Streams.Stream stream)
    {
        return _justAddedStreams.Contains(stream);
    }

    public void UpdateStreams()
    {
        userStreams.Update();
    }

    private void OnStreamAdded(ReplicatedDictionary<RefID, Stream> collection, RefID key, Stream stream, bool isNew)
    {
        World.ReferenceController.AllocationBlockBegin(key);
        stream.Initialize(this);
        World.ReferenceController.AllocationBlockEnd();

        if (IsLocal)
        {
            stream.Group = "Default";
            stream.Active = true;
        }
        else
        {
            _justAddedStreams.Add(stream);
        }
    }

    private void OnStreamRemoved(ReplicatedDictionary<RefID, Stream> collection, RefID key, Stream stream)
    {
        _justAddedStreams.Remove(stream);
        stream.Dispose();
    }

    internal void ClearJustAddedStreams()
    {
        _justAddedStreams.Clear();
    }

    /// <summary>
    /// Get tracking streams for a body node.
    /// </summary>
    public void GetTrackingStreams(BodyNode node, out Float3ValueStream position, out FloatQValueStream rotation)
    {
        EnsureTrackingStreamsInitialized();

        if (!_trackingStreams.TryGetValue(node, out var pair))
        {
            pair = CreateTrackingStreamPair();
            _trackingStreams[node] = pair;
        }

        position = pair.Position;
        rotation = pair.Rotation;
    }

    internal void ConfigureLocalTrackingStreams()
    {
        foreach (var stream in Streams)
        {
            stream.Active = true;

            if (stream is ImplicitStream implicitStream)
            {
                implicitStream.SetUpdatePeriod(1, 0);
            }

            StreamGroupManager.AssignToGroup(stream, null);
        }

        if (_trackingStreamsInitialized)
        {
            foreach (var pair in _trackingStreams.Values)
            {
                pair.Position.Active = true;
                pair.Rotation.Active = true;

                pair.Position.SetUpdatePeriod(1, 0);
                pair.Rotation.SetUpdatePeriod(1, 0);

                StreamGroupManager.AssignToGroup(pair.Position, null);
                StreamGroupManager.AssignToGroup(pair.Rotation, null);
            }
        }

        LumoraLogger.Log($"ConfigureLocalTrackingStreams: Configured {StreamCount} streams for local user");
    }

    private void EnsureTrackingStreamsInitialized()
    {
        if (_trackingStreamsInitialized)
            return;

        _trackingStreamsInitialized = true;

        foreach (var node in TrackingNodes)
        {
            if (_trackingStreams.ContainsKey(node))
                continue;

            _trackingStreams[node] = CreateTrackingStreamPair();
        }
    }

    private TrackingStreamPair CreateTrackingStreamPair()
    {
        var position = CreateTrackingStream<Float3ValueStream>();
        var rotation = CreateTrackingStream<FloatQValueStream>();
        return new TrackingStreamPair(position, rotation);
    }

    private T CreateTrackingStream<T>() where T : Stream, new()
    {
        var stream = new T();
        RefID key = World.ReferenceController.PeekID();
        userStreams.Add(key, stream, isNewlyCreated: true);

        stream.InitializeDefaults(active: true, groupName: TrackingGroupName);

        if (IsLocal)
        {
            if (stream is ImplicitStream implicitStream)
            {
                implicitStream.SetUpdatePeriod(1, 0);
            }
        }

        return stream;
    }

    #endregion

    public override void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        UserRootRef.OnTargetChange -= OnUserRootRefChanged;
        userStreams.OnElementAdded -= OnStreamAdded;
        userStreams.OnElementRemoved -= OnStreamRemoved;

        userStreams.Clear();
        StreamGroupManager?.Clear();
        _trackingStreams.Clear();
        _justAddedStreams.Clear();

        base.Dispose();
    }

    private void OnUserRootRefChanged(SyncRef<Components.UserRoot> syncRef)
    {
        var userRoot = syncRef.Target;
        if (userRoot == null || userRoot.IsDestroyed)
        {
            return;
        }

        if (userRoot.ActiveUser == this)
        {
            return;
        }

        // Only the authority should initialize and bind UserRoot.
        // Clients should not modify synced references during callbacks.
        if (World?.IsAuthority == true)
        {
            if (userRoot.ActiveUser != null && userRoot.ActiveUser != this)
            {
                LumoraLogger.Warn(
                    $"User: UserRootRef points to UserRoot owned by '{userRoot.ActiveUser.UserName.Value ?? "(null)"}', rebinding to '{UserName.Value ?? "(null)"}'");
            }

            userRoot.Initialize(this);
        }
    }

    public override string ToString()
    {
        return $"User({UserName.Value ?? UserID.Value}, ID={ReferenceID})";
    }
}

