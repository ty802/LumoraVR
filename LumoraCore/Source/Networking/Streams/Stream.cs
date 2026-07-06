// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

/// <summary>
/// Base class for all network streams. Streams provide high-frequency, unreliable data synchronization.
/// Derives from Worker, so sync members wire automatically; the payload itself rides Encode/Decode. -xlinka
/// </summary>
public abstract class Stream : Worker, IStream
{
    protected readonly Sync<bool> _active = new();
    protected readonly Sync<ushort> _group = new();
    protected readonly Sync<string> _name = new();

    private bool _groupAssigned;
    private ushort? _oldGroup;
    private string _groupName = null!;

    /// <summary>
    /// The user that owns this stream.
    /// </summary>
    public User Owner { get; private set; } = null!;

    /// <summary>
    /// Whether this stream belongs to the local user.
    /// </summary>
    public bool IsLocal => Owner == World?.LocalUser;

    /// <summary>
    /// Streams are not persistent (not saved).
    /// </summary>
    public override bool IsPersistent => false;

    /// <summary>
    /// Whether this stream has valid data to read.
    /// </summary>
    public abstract bool HasValidData { get; }

    /// <summary>
    /// Whether this stream is actively transmitting/receiving.
    /// </summary>
    public bool Active
    {
        get => _active.Value;
        set
        {
            CheckOwnership(allowJustAdded: false);
            _active.Value = value;
        }
    }

    /// <summary>
    /// Numeric index of the stream group.
    /// </summary>
    public ushort GroupIndex => _group.Value;

    /// <summary>
    /// Name of the stream group this stream belongs to.
    /// </summary>
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

    /// <summary>
    /// Name of this stream. Replicates, so every peer agrees - this is what name-based lookup
    /// (User.GetStreamOrAdd) keys on, e.g. a per-user "Voice" stream. -xlinka
    /// </summary>
    public string Name
    {
        get => _name.Value;
        set
        {
            CheckOwnership(allowJustAdded: false);
            _name.Value = value;
        }
    }

    /// <summary>
    /// Whether this stream uses implicit (periodic) updates.
    /// </summary>
    public bool IsImplicit => Period != 0;

    /// <summary>
    /// Update period in sync ticks for implicit streams.
    /// </summary>
    public abstract uint Period { get; }

    /// <summary>
    /// Phase offset for implicit updates.
    /// </summary>
    public abstract uint Phase { get; }

    /// <summary>
    /// Initialize this stream with its owning user via the standard worker pipeline (which wires every
    /// sync member), then OnInit, then ends the init phase. Called inside User.OnStreamAdded's allocation block. -xlinka
    /// </summary>
    internal void Initialize(User user)
    {
        Owner = user;
        InitializeWorker(user.World, user);
        OnInit();
        EndInitializationStageForMembers();
    }

    /// <summary>
    /// Initialize stream defaults without ownership checks. Intended for setup during user initialization.
    /// Runs after <see cref="Initialize"/>, so the members are already wired.
    /// </summary>
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

    /// <summary>
    /// Check if this stream should send an explicit update at the given time point.
    /// </summary>
    public abstract bool IsExplicitUpdatePoint(ulong timePoint);

    /// <summary>
    /// Check if this stream should send an implicit update at the given time point.
    /// </summary>
    public bool IsImplicitUpdatePoint(ulong timePoint)
    {
        if (!IsImplicit)
            return false;
        return (timePoint - Phase) % Period == 0;
    }

    /// <summary>
    /// Check that the current user owns this stream.
    /// </summary>
    protected void CheckOwnership(bool allowJustAdded = true)
    {
        if (World?.LocalUser != Owner && (Active || !Owner.WasStreamJustAdded(this) || !allowJustAdded))
        {
            throw new InvalidOperationException("Only User owning the stream can modify it, unless it was just added!");
        }
    }

    /// <summary>
    /// Encode stream data to the writer.
    /// </summary>
    public abstract void Encode(BinaryWriter writer);

    /// <summary>
    /// Decode stream data from the reader.
    /// </summary>
    public abstract void Decode(BinaryReader reader, StreamMessage message);

    /// <summary>
    /// Called every frame to update the stream.
    /// </summary>
    public virtual void Update()
    {
    }

    /// <summary>
    /// Called when the stream is initialized (after its members are wired, before their init phase ends).
    /// </summary>
    protected virtual void OnInit()
    {
    }

    // NOTE: there is no off-world-thread codec seam here. SyncController encodes/decodes streams
    // synchronously on the sync thread. An earlier async-encode/decode scaffold lived here but was never
    // wired into the send/receive path, so it was removed to avoid implying a capability that didn't
    // exist. A real heavy codec (e.g. voice) would add that seam together with the codec itself. -xlinka

    /// <summary>
    /// Get hierarchy path for debugging.
    /// </summary>
    public override string ParentHierarchyToString() => $"Stream:{GetType().Name}@{Owner?.UserName?.Value ?? "?"}";

    /// <summary>
    /// Dispose of this stream.
    /// </summary>
    public override void Dispose()
    {
        Owner = null!;
        base.Dispose();
    }
}
