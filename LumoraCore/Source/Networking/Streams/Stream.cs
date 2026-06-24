// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

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

    // ---- ASYNC CODEC INFRA ----
    // Streams with an expensive codec (voice) run their Encode/Decode off the world thread so the heavy
    // work doesn't stall it. The background dispatch + decode sequencing live here. The codec itself, and
    // the hook into the send/receive path, are not built.
    // TODO(techy): implement InternalAsyncEncode/InternalAsyncDecode (the codec), and call RunAsyncEncode /
    // QueueAsyncDecode from the SyncController stream path (SyncController.cs ~ stream.Encode) for streams
    // where SupportsAsyncCodec is true. -xlinka

    /// <summary>True on streams whose Encode/Decode is heavy enough to run off the world thread.</summary>
    public virtual bool SupportsAsyncCodec => false;

    /// <summary>When true, queued async decodes run one at a time in arrival order.</summary>
    protected virtual bool SequenceAsyncDecodes => true;

    private readonly object _asyncDecodeLock = new();
    private readonly Queue<(byte[] data, StreamMessage message)> _asyncDecodeQueue = new();
    private bool _asyncDecodeRunning;

    /// <summary>
    /// Run the (heavy) encode on a background task; the encoded bytes arrive via <paramref name="onEncoded"/>.
    /// </summary>
    protected void RunAsyncEncode(Action<byte[]> onEncoded)
    {
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream();
                var writer = new BinaryWriter(ms);
                InternalAsyncEncode(writer);
                writer.Flush();
                onEncoded(ms.ToArray());
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"Stream async encode failed ({GetType().Name}): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Queue an incoming frame for background decode (sequenced when <see cref="SequenceAsyncDecodes"/>).
    /// </summary>
    protected void QueueAsyncDecode(byte[] data, StreamMessage message)
    {
        lock (_asyncDecodeLock)
        {
            _asyncDecodeQueue.Enqueue((data, message));
            if (_asyncDecodeRunning && SequenceAsyncDecodes)
                return;
            _asyncDecodeRunning = true;
        }
        Task.Run(DrainAsyncDecodeQueue);
    }

    private void DrainAsyncDecodeQueue()
    {
        while (true)
        {
            byte[] data;
            StreamMessage message;
            lock (_asyncDecodeLock)
            {
                if (_asyncDecodeQueue.Count == 0)
                {
                    _asyncDecodeRunning = false;
                    return;
                }
                (data, message) = _asyncDecodeQueue.Dequeue();
            }

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                InternalAsyncDecode(reader, message);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"Stream async decode failed ({GetType().Name}): {ex.Message}");
            }
        }
    }

    /// <summary>Background-thread encode hook. Override with the codec. TODO(techy): implement.</summary>
    protected virtual void InternalAsyncEncode(BinaryWriter writer)
    {
        // TODO(techy): codec encode (e.g. Opus frame) on the background thread.
    }

    /// <summary>Background-thread decode hook. Override with the codec. TODO(techy): implement.</summary>
    protected virtual void InternalAsyncDecode(BinaryReader reader, StreamMessage message)
    {
        // TODO(techy): codec decode (e.g. Opus frame) on the background thread.
    }

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
