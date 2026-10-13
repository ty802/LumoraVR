// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

// Base class for all network streams. Streams provide high-frequency, unreliable data synchronization.
// Derives from Worker, so sync members wire automatically; the payload itself rides Encode/Decode. -xlinka
public abstract class Stream : Worker, IStream
{
    protected readonly Sync<bool> _active = new();
    protected readonly Sync<ushort> _group = new();
    protected readonly Sync<string> _name = new();

    private bool _groupAssigned;
    private ushort? _oldGroup;
    private string _groupName = null!;

    public User Owner { get; private set; } = null!;

    public bool IsLocal => Owner == World?.LocalUser;

    public override bool IsPersistent => false;

    public abstract bool HasValidData { get; }

    public bool Active
    {
        get => _active.Value;
        set
        {
            CheckOwnership(allowJustAdded: false);
            _active.Value = value;
        }
    }

    public ushort GroupIndex => _group.Value;

    public string Group
    {
        get => _groupName ?? ("Group " + GroupIndex);
        set
        {
            CheckOwnership(allowJustAdded: false);
            _groupName = value;
            _group.Value = Owner.StreamGroupManager.GetGroupIndex(value);
        }
    }

    // Name of this stream. Replicates, so every peer agrees - this is what name-based lookup
    // (User.GetStreamOrAdd) keys on, e.g. a per-user "Voice" stream. -xlinka
    public string Name
    {
        get => _name.Value;
        set
        {
            CheckOwnership(allowJustAdded: false);
            _name.Value = value;
        }
    }

    public bool IsImplicit => Period != 0;

    // In sync ticks.
    public abstract uint Period { get; }

    public abstract uint Phase { get; }

    // Initialize this stream with its owning user via the standard worker pipeline (which wires every
    // sync member), then OnInit, then ends the init phase. Called inside User.OnStreamAdded's allocation block. -xlinka
    internal void Initialize(User user)
    {
        Owner = user;
        InitializeWorker(user.World, user);
        OnInit();
        EndInitializationStageForMembers();
    }

    // No ownership checks, for setup during user init. Runs after Initialize, so the members are already wired.
    internal void InitializeDefaults(bool active, string groupName)
    {
        _active.SetValueSilently(active, change: false);
        _groupName = groupName;
        if (Owner != null)
        {
            var groupIndex = Owner.StreamGroupManager.GetGroupIndex(groupName);
            _group.SetValueSilently(groupIndex, change: false);
            _oldGroup = groupIndex;
            Owner.StreamGroupManager.AssignToGroup(this, null);
            _groupAssigned = true;
        }

        // Silent, so no ownership check and no change handlers, but still sent: a peer already in the
        // session meets this stream by delta and drops the data of an inactive one. -xlinka
        _active.InvalidateSyncElement();
        _group.InvalidateSyncElement();
    }

    // Group reassignment rides the worker's member-change hook now, not a manual _group.Changed sub. -xlinka
    protected override void SyncMemberChanged(IChangeable member)
    {
        if (member != _group)
        {
            return;
        }

        if (Owner?.StreamGroupManager != null)
        {
            Owner.StreamGroupManager.AssignToGroup(this, _oldGroup);
            _groupAssigned = true;
            _oldGroup = GroupIndex;
        }

        if (World?.State == Lumora.Core.World.WorldState.Running && IsInitialized && _groupAssigned)
        {
            Owner?.StreamGroupManager?.StreamModified(this);
        }
    }

    public abstract bool IsExplicitUpdatePoint(ulong timePoint);

    public bool IsImplicitUpdatePoint(ulong timePoint)
    {
        if (!IsImplicit)
            return false;
        return (timePoint - Phase) % Period == 0;
    }

    protected void CheckOwnership(bool allowJustAdded = true)
    {
        if (World?.LocalUser != Owner && (Active || !Owner.WasStreamJustAdded(this) || !allowJustAdded))
        {
            throw new InvalidOperationException("Only User owning the stream can modify it, unless it was just added!");
        }
    }

    public abstract void Encode(BinaryWriter writer);

    public abstract void Decode(BinaryReader reader, StreamMessage message);

    // Every frame.
    public virtual void Update()
    {
    }

    // Members are wired by now; their init phase has not ended yet.
    protected virtual void OnInit()
    {
    }

    // NOTE: there is no off-world-thread codec seam here. SyncController encodes/decodes streams
    // synchronously on the sync thread. An earlier async-encode/decode scaffold lived here but was never
    // wired into the send/receive path, so it was removed to avoid implying a capability that didn't
    // exist. A real heavy codec (e.g. voice) would add that seam together with the codec itself. -xlinka

    public override string ParentHierarchyToString() => $"Stream:{GetType().Name}@{Owner?.UserName?.Value ?? "?"}";

    public override void Dispose()
    {
        // A removed stream must leave its send group before its fields are disposed. Otherwise the
        // sender keeps encoding its dead RefID and receivers cannot decode the rest of that group.
        Owner?.StreamGroupManager?.RemoveStream(this);
        Owner = null!;
        base.Dispose();
    }
}
