using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;
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
public class User : ISyncObject, IWorldElement, IDisposable
{
    private RefID _referenceID;
    private List<ISyncMember> _syncMembers;

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
    public Lumora.Core.Networking.Sync.UserStreamBag StreamBag { get; private set; } = new();

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
            if (World?.LocalUser == this)
            {
                _root = value;
                UserRootRef.Target = value;
                AquaLogger.Log($"User: Registered UserRoot for local user '{UserName.Value}'");
            }
            else
            {
                throw new Exception("Only the user can register their own UserRoot");
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

        // Discover sync members
        _syncMembers = SyncMemberDiscovery.DiscoverSyncMembers(this);

        // Initialize sync members with RefIDs
        InitializeSyncMemberRefIDs(fromNetwork);

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

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        IsDestroyed = true;
        World = null;
    }

    public override string ToString()
    {
        return $"User({UserName.Value ?? UserID.Value}, ID={ReferenceID})";
    }
}
