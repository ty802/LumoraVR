// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Scripting;

// Script undo across machines. A Shared script's batch closes on the host and belongs to the person whose
// action set its call off, else to the script's author. The host holds it and offers it on that person's
// relay; their machine puts it in their own history, and their undo and redo come back as requests. The
// host takes a request only from the person the batch belongs to, and puts the batch back as the script's
// principal. Undos run newest first and redos oldest first, so several in one frame land in order.
//
// A batch goes when its owner's history lets it go, when its owner or its script's author leaves, when its
// owner holds MaxHeldPerPerson newer ones, or when it can no longer be put back. Its owner's machine is told
// and drops it the way it drops any step whose targets are gone. -xlinka
public sealed class ScriptUndoHub
{
    public const int MaxHeldPerPerson = 64;

    // How long a batch waits for its owner's machine to have a relay before it is let go.
    public const double RelayWaitSeconds = 30.0;

    private sealed class Held
    {
        public int Id;
        public ScriptUndoBatch Batch = null!;
        public double At;
        public ScriptUndoRelay? OfferedOn;
        public bool Dropped;
    }

    private readonly World _world;
    private readonly Dictionary<User, List<Held>> _held = new();
    private readonly List<ScriptUndoRelay> _relays = new();
    private readonly List<User> _owners = new();
    private readonly HashSet<RefID> _ignored = new();
    private readonly Dictionary<int, ScriptUndoRequest> _requests = new();
    private int _nextId;
    private ScriptUndoRelay? _mine;

    // Batches the host holds, and what it has done with requests.
    public int HeldCount
    {
        get
        {
            int count = 0;
            foreach (var list in _held.Values)
                count += list.Count;
            return count;
        }
    }

    public int Undone { get; private set; }
    public int Redone { get; private set; }
    public int Refused { get; private set; }

    // Requests found in someone's relay that they did not write, counted up to MaxIgnoredCounted.
    public int Ignored => _ignored.Count;
    public const int MaxIgnoredCounted = 1024;

    // Entries of a relay's requests the host reads each frame. An owner needs one per batch held for them;
    // a list past this is someone flooding their own relay, and only their own undo goes unread.
    public const int MaxRequestsRead = 4 * MaxHeldPerPerson;

    internal ScriptUndoHub(World world)
    {
        _world = world;
    }

    // The history of the person at this machine.
    public static UndoManager? LocalHistory(World? world) => world?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();

    internal void Register(ScriptUndoRelay relay)
    {
        if (!_relays.Contains(relay))
            _relays.Add(relay);
    }

    internal void Unregister(ScriptUndoRelay relay) => _relays.Remove(relay);

    internal void Offer(User owner, ScriptUndoBatch batch)
    {
        if (!_held.TryGetValue(owner, out var list))
        {
            list = new List<Held>();
            _held[owner] = list;
        }
        int live = 0;
        foreach (var held in list)
        {
            if (!held.Dropped)
                live++;
        }
        for (int i = 0; i < list.Count && live >= MaxHeldPerPerson; i++)
        {
            if (list[i].Dropped)
                continue;
            Drop(list[i]);
            live--;
        }
        // What was dropped waits for the owner's machine to let it go; one that never does still has a bound.
        for (int i = 0; i < list.Count && list.Count >= 2 * MaxHeldPerPerson; )
        {
            if (list[i].Dropped)
                Let(list, i);
            else
                i++;
        }
        list.Add(new Held { Id = ++_nextId, Batch = batch, At = _world.Time.TotalTime });
    }

    internal void Update(double now)
    {
        if (!_world.IsAuthority)
        {
            // Made only in a world that has scripts, so joining a world without any makes nothing.
            if (_mine is not { IsDestroyed: false } && _world.ScriptsIfAny?.HostCount > 0)
                _mine = ScriptUndoRelay.For(_world);
            return;
        }
        if (_held.Count == 0)
            return;
        _owners.Clear();
        _owners.AddRange(_held.Keys);
        foreach (var owner in _owners)
        {
            var list = _held[owner];
            if (owner.IsDestroyed || !ReferenceEquals(owner.World, _world))
            {
                foreach (var held in list)
                {
                    if (!held.Dropped)
                        held.Batch.Evict();
                }
                _held.Remove(owner);
                continue;
            }
            Settle(owner, list, RelayOf(owner), now);
            if (list.Count == 0)
                _held.Remove(owner);
        }
    }

    // Someone left: a batch whose script's author is gone can never be put back.
    internal void Recheck()
    {
        foreach (var list in _held.Values)
        {
            foreach (var held in list)
            {
                if (!held.Dropped && !held.Batch.Principal.IsStillAuthorIn(_world))
                    Drop(held);
            }
        }
    }

    private void Settle(User owner, List<Held> list, ScriptUndoRelay? relay, double now)
    {
        // Offered on a relay that is gone, which took the history holding it with it; dropped before it was
        // offered; or waited too long for a relay.
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var held = list[i];
            if (held.OfferedOn != null ? !ReferenceEquals(held.OfferedOn, relay)
                : held.Dropped || (relay == null && now - held.At > RelayWaitSeconds))
                Let(list, i);
        }
        if (relay == null)
            return;
        // In the order they closed, which is the order the owner's history keeps them in.
        foreach (var held in list)
        {
            if (held.OfferedOn == null)
                Offer(relay, held);
        }

        ReadRequests(relay, owner);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var held = list[i];
            if (!held.Dropped && held.OfferedOn != null && _requests.TryGetValue(held.Id, out var request) && request.Undone.Value
                && !held.Batch.IsUndone)
                Apply(held, undo: true);
        }
        for (int i = 0; i < list.Count; i++)
        {
            var held = list[i];
            if (!held.Dropped && held.OfferedOn != null && _requests.TryGetValue(held.Id, out var request) && !request.Undone.Value
                && held.Batch.IsUndone)
                Apply(held, undo: false);
        }
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var held = list[i];
            if (held.OfferedOn != null && _requests.TryGetValue(held.Id, out var request) && request.LetGo.Value)
                Let(list, i);
        }
    }

    private void Apply(Held held, bool undo)
    {
        if (held.Batch.Apply(undo))
        {
            if (undo)
                Undone++;
            else
                Redone++;
            return;
        }
        Refused++;
        Drop(held);
    }

    // The batch can never be put back: let go of here, and its owner's machine told.
    private void Drop(Held held)
    {
        held.Dropped = true;
        held.Batch.Evict();
        if (held.OfferedOn is { IsDestroyed: false } relay && relay.FindOffer(held.Id) is { } offer)
        {
            using (_world.DataModelPermissions?.EnterSystemBypass())
                offer.Dropped.Value = true;
        }
    }

    // The batch leaves for good, and its offer with it.
    private void Let(List<Held> list, int index)
    {
        var held = list[index];
        list.RemoveAt(index);
        if (!held.Dropped)
            held.Batch.Evict();
        if (held.OfferedOn is { IsDestroyed: false } relay && relay.FindOffer(held.Id) is { } offer)
        {
            using (_world.DataModelPermissions?.EnterSystemBypass())
                relay.Offers.Remove(offer);
        }
    }

    // Host bookkeeping on the owner's own component, so it takes the system scope.
    private void Offer(ScriptUndoRelay relay, Held held)
    {
        using (_world.DataModelPermissions?.EnterSystemBypass())
        {
            var offer = relay.Offers.Add();
            offer.Id.Value = held.Id;
            offer.Description.Value = held.Batch.Description;
            if (held.Batch.Principal.Author is { IsDestroyed: false } author)
                offer.Author.Target = author;
        }
        held.OfferedOn = relay;
    }

    // The owner's relay: in their byte, minted by their own machine rather than by the host for their scripts.
    private ScriptUndoRelay? RelayOf(User owner)
    {
        byte owned = owner.AllocationID.Value;
        var controller = _world.ReferenceController;
        for (int i = _relays.Count - 1; i >= 0; i--)
        {
            var relay = _relays[i];
            if (relay.IsDestroyed)
            {
                _relays.RemoveAt(i);
                continue;
            }
            if (relay.ReferenceID.GetUserByte() == owned && controller?.BelowScriptFence(relay.ReferenceID) != false)
                return relay;
        }
        return null;
    }

    // The owner's own requests by batch. One someone else put in the list or last wrote is not theirs.
    private void ReadRequests(ScriptUndoRelay relay, User owner)
    {
        _requests.Clear();
        byte owned = owner.AllocationID.Value;
        var requests = relay.Requests;
        for (int i = 0; i < requests.Count && i < MaxRequestsRead; i++)
        {
            var request = requests[i];
            if (request.ReferenceID.GetUserByte() == owned && WrittenBy(request.Id, owner) && WrittenBy(request.Undone, owner)
                && WrittenBy(request.LetGo, owner))
                _requests.TryAdd(request.Id.Value, request);
            else if (_ignored.Count < MaxIgnoredCounted)
                _ignored.Add(request.ReferenceID);
        }
    }

    // A null writer is a value that came with the element, from the machine that made it.
    private static bool WrittenBy(ConflictingSyncElement member, User owner)
        => member.LastModifyingUser is not { } writer || ReferenceEquals(writer, owner);
}
