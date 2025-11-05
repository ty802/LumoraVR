using System;
using System.Collections.Generic;
using Godot;
using Aquamarine.Source.Networking.Sync;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// Represents a user in the world.
/// 
/// </summary>
public class User : ISyncObject, IWorldElement, IDisposable
{
    private ulong _referenceID;
    private List<ISyncMember> _syncMembers;

    // Sync members - auto-discovered
    public readonly Aquamarine.Source.Networking.Sync.Sync<string> UserName = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<string> UserID = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<ulong> AllocationIDStart = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<ulong> AllocationIDEnd = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<int> Ping = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<bool> IsPresent = new();
    public readonly Aquamarine.Source.Networking.Sync.Sync<bool> IsSilenced = new();

    // Network statistics
    public ulong SentBytes { get; set; }
    public ulong ReceivedBytes { get; set; }
    public DateTime LastSyncMessage { get; set; }
    public int ImmediateDeltaCount { get; set; }
    public int ImmediateControlCount { get; set; }
    public int ImmediateStreamCount { get; set; }

    // ISyncObject implementation
    public List<ISyncMember> SyncMembers => _syncMembers;
    public ulong ReferenceID => _referenceID;
    public bool IsAuthority => World?.IsAuthority ?? false;

    // IWorldElement implementation
    public ulong RefID => _referenceID;
    public World World { get; private set; }
    public bool IsDestroyed { get; private set; }
    public bool IsInitialized { get; private set; } = true;

    public bool IsHost => World?.IsAuthority == true && World.LocalUser == this;
    public bool IsDisposed { get; private set; }

    public void Destroy()
    {
        Dispose();
    }

    // Stream control
    public bool ReceiveStreams { get; set; } = true;
    public Aquamarine.Source.Networking.Sync.UserStreamBag StreamBag { get; private set; } = new();

    public User(World world, ulong refID)
    {
        World = world;
        _referenceID = refID;

        // Discover sync members
        _syncMembers = SyncMemberDiscovery.DiscoverSyncMembers(this);

        AquaLogger.Debug($"User created with {_syncMembers.Count} sync members");
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
    /// Allocate a new Reference ID for this user.
    /// Each user has a unique range of IDs they can allocate.
    /// </summary>
    public ulong AllocateReferenceID()
    {
        ulong id = AllocationIDStart.Value;
        AllocationIDStart.Value++;

        if (AllocationIDStart.Value >= AllocationIDEnd.Value)
        {
            AquaLogger.Error($"User {UserName.Value} ran out of allocation IDs!");
        }

        return id;
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
        return $"User({UserName.Value ?? UserID.Value})";
    }
}
