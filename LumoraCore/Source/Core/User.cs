using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Networking.Streams;
using AquaLogger = Lumora.Core.Logging.Logger;

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
public class User : ISyncObject, IWorldElement, IDisposable, IWorker
{
    private RefID _referenceID;
    private List<ISyncMember> _syncMembers;
    private readonly Dictionary<BodyNode, TrackingStreamPair> _trackingStreams = new();
    private bool _trackingStreamsInitialized;

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

    // Sync members - auto-discovered
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

    // Network statistics (non-synced)
    public ulong SentBytes { get; set; }
    public ulong ReceivedBytes { get; set; }
    public DateTime LastSyncMessage { get; set; }
    public int ImmediateDeltaCount { get; set; }
    public int ImmediateControlCount { get; set; }
    public int ImmediateStreamCount { get; set; }

    // ISyncObject implementation
    public List<ISyncMember> SyncMembers => _syncMembers;
    public RefID ReferenceID => _referenceID;
    public ulong RefIdNumeric => (ulong)_referenceID;
    public bool IsAuthority => World?.IsAuthority ?? false;

    // IWorldElement implementation
    public World World { get; private set; }
    public bool IsDestroyed { get; private set; }
    public bool IsInitialized { get; private set; } = true;
    public bool IsLocalElement => ReferenceID.IsLocalID;
    public bool IsPersistent => true;
    public Type WorkerType => GetType();
    public string WorkerTypeName => WorkerType.FullName;

    /// <summary>
    /// Try to get a field by name (IWorker implementation).
    /// </summary>
    public IField TryGetField(string name)
    {
        var type = GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field != null && typeof(IField).IsAssignableFrom(field.FieldType))
        {
            return field.GetValue(this) as IField;
        }
        return null;
    }

    /// <summary>
    /// Try to get a typed field by name (IWorker implementation).
    /// </summary>
    public IField<T> TryGetField<T>(string name)
    {
        return TryGetField(name) as IField<T>;
    }

    /// <summary>
    /// Get all referenced objects from this user (IWorker implementation).
    /// </summary>
    public IEnumerable<IWorldElement> GetReferencedObjects(bool assetRefOnly, bool persistentOnly = true)
    {
        // User doesn't reference other world elements directly
        yield break;
    }

    /// <summary>
    /// Hierarchy path for debugging.
    /// </summary>
    public string ParentHierarchyToString() => $"User:{UserName.Value ?? UserID.Value}";

    public bool IsHost => World?.IsAuthority == true && World.LocalUser == this;
    public bool IsDisposed { get; private set; }

    public void Destroy()
    {
        Dispose();
    }

    // Stream control
    public bool ReceiveStreams { get; set; } = true;

    /// <summary>
    /// Legacy stream bag for simple stream data (transforms, etc.)
    /// </summary>
    public Lumora.Core.Networking.Sync.UserStreamBag LegacyStreamBag { get; private set; } = new();

    /// <summary>
    /// New stream bag for full stream system.
    /// </summary>
    public StreamBag Streams { get; private set; }

    /// <summary>
    /// Stream group manager for organizing streams.
    /// </summary>
    public StreamGroupManager StreamGroupManager { get; private set; }

    /// <summary>
    /// Stream configuration version - incremented when streams change.
    /// </summary>
    public uint StreamConfigurationVersion { get; private set; }

    /// <summary>
    /// Whether this is the local user.
    /// </summary>
    public bool IsLocal => World?.LocalUser == this;

    private Components.UserRoot _root;
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
                    AquaLogger.Log($"User: Registered UserRoot for {scope} user '{UserName.Value}'");
                }
            }
            else
            {
                throw new Exception("Only the user or authority can register their UserRoot");
            }
        }
    }

    /// <summary>
    /// Create a User for the local/host world (allocates RefIDs normally).
    /// </summary>
    public User(World world, RefID refID) : this(world, refID, fromNetwork: false)
    {
    }

    /// <summary>
    /// Create a User, optionally from network (uses allocation block for network-assigned RefIDs).
    /// </summary>
    internal User(World world, RefID refID, bool fromNetwork)
    {
        World = world;
        _referenceID = refID;

        // Initialize stream infrastructure
        StreamGroupManager = new StreamGroupManager(this);
        Streams = new StreamBag();
        Streams.Initialize(this);

        // Discover sync members
        _syncMembers = SyncMemberDiscovery.DiscoverSyncMembers(this);

        // Initialize sync members with RefIDs
        InitializeSyncMemberRefIDs(fromNetwork);

        // Bind UserRootRef changes to keep UserRoot.ActiveUser in sync
        UserRootRef.OnTargetChange += OnUserRootRefChanged;

        AquaLogger.Debug($"User created with {_syncMembers.Count} sync members (fromNetwork={fromNetwork})");
    }

    /// <summary>
    /// Initialize sync members with RefIDs.
    /// For network-created users, uses allocation block to match host's RefID pattern.
    /// For locally-created users, uses normal sequential allocation.
    /// </summary>
    private void InitializeSyncMemberRefIDs(bool fromNetwork)
    {
        if (World == null) return;

        var refController = World.ReferenceController;
        if (refController == null) return;

        // Register the user itself first
        refController.RegisterObject(this);

        if (fromNetwork)
        {
            // Network-created: Use allocation block to match host's RefID pattern
            // Host allocated User at X, sync members at X+1, X+2, etc.
            var nextId = RefID.Construct(_referenceID.GetUserByte(), _referenceID.GetPosition() + 1);
            AquaLogger.Debug($"User.InitializeSyncMemberRefIDs: Starting allocation block at {nextId} for {_syncMembers.Count} members");
            refController.AllocationBlockBegin(nextId);
            try
            {
                int memberIndex = 0;
                foreach (var member in _syncMembers)
                {
                    if (member is SyncElement syncElement)
                    {
                        syncElement.Initialize(World, this);
                        AquaLogger.Debug($"  [{memberIndex}] {member.Name} → {syncElement.ReferenceID}");
                        memberIndex++;
                    }
                }

                EnsureTrackingStreamsInitialized();
            }
            finally
            {
                refController.AllocationBlockEnd();
            }
        }
        else
        {
            // Locally-created: Normal sequential allocation
            AquaLogger.Debug($"User.InitializeSyncMemberRefIDs: Sequential allocation for {_syncMembers.Count} members");
            int memberIndex = 0;
            foreach (var member in _syncMembers)
            {
                if (member is SyncElement syncElement)
                {
                    syncElement.Initialize(World, this);
                    AquaLogger.Debug($"  [{memberIndex}] {member.Name} → {syncElement.ReferenceID}");
                    memberIndex++;
                }
            }

            EnsureTrackingStreamsInitialized();
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
            if (syncManager != null && ReferenceID == syncManager.LocalUserRefIDToInit)
            {
                World.SetLocalUser(this);
                AquaLogger.Log($"User.Initialize: Set local user '{UserName.Value}' (RefID: {ReferenceID})");
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
            AquaLogger.Warn("Non-authority user cannot change username");
        }
    }

    /// <summary>
    /// Get all dirty sync members that need to be sent over network.
    /// </summary>
    public List<ISyncMember> GetDirtySyncMembers()
    {
        return SyncMemberDiscovery.GetDirtySyncMembers(_syncMembers);
    }

    /// <summary>
    /// Clear dirty flags after sync.
    /// </summary>
    public void ClearDirtyFlags()
    {
        SyncMemberDiscovery.ClearDirtyFlags(_syncMembers);
    }

    #region Stream Management

    /// <summary>
    /// Add a stream to this user.
    /// </summary>
    public S AddStream<S>() where S : Networking.Streams.Stream, new()
    {
        var stream = new S();
        stream.Initialize(this);
        Streams.Add(stream);

        if (IsLocal)
        {
            stream.Group = "Default";
            stream.Active = true;
        }

        return stream;
    }

    /// <summary>
    /// Remove a stream from this user.
    /// </summary>
    public void RemoveStream(Networking.Streams.Stream stream)
    {
        Streams.Remove(stream);
        stream.Dispose();
    }

    /// <summary>
    /// Called when stream configuration changes.
    /// </summary>
    public void StreamConfigurationChanged()
    {
        StreamConfigurationVersion++;
    }

    /// <summary>
    /// Check if a stream was just added (can be modified by non-owner briefly).
    /// </summary>
    public bool WasStreamJustAdded(Networking.Streams.Stream stream)
    {
        return Streams.WasJustAdded(stream);
    }

    /// <summary>
    /// Update all streams for this user.
    /// </summary>
    public void UpdateStreams()
    {
        Streams.Update();
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

    /// <summary>
    /// Configure tracking stream periods once this user becomes local.
    /// </summary>
    internal void ConfigureLocalTrackingStreams()
    {
        if (!_trackingStreamsInitialized)
            return;

        foreach (var pair in _trackingStreams.Values)
        {
            pair.Position.SetUpdatePeriod(1, 0);
            pair.Rotation.SetUpdatePeriod(1, 0);
        }
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
        stream.Initialize(this);
        Streams.Add(stream);

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

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        IsDestroyed = true;

        UserRootRef.OnTargetChange -= OnUserRootRefChanged;

        // Clean up streams
        Streams?.Clear();
        StreamGroupManager?.Clear();
        _trackingStreams.Clear();

        World = null;
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

        if (userRoot.ActiveUser != null && userRoot.ActiveUser != this)
        {
            AquaLogger.Warn(
                $"User: UserRootRef points to UserRoot owned by '{userRoot.ActiveUser.UserName.Value ?? "(null)"}', rebinding to '{UserName.Value ?? "(null)"}'");
        }

        userRoot.Initialize(this);
    }

    public override string ToString()
    {
        return $"User({UserName.Value ?? UserID.Value}, ID={ReferenceID})";
    }
}
