// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// Carries script undo between a person's machine and the host. A Shared script runs on the host, so what it
// records for someone is held there; this hands it to that person's own history and carries their undo and
// redo back.
//
// Each machine mints its person's relay in its own byte under their user root, as a press relay is, while
// the world has scripts. The host writes what it offers, host-only; the owner writes what they want of each
// batch. The host reads the owner from the byte, never from a field, and takes nothing anyone else wrote.
// -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptUndoRelay : Component
{
    private const string RelaySlotName = "ScriptUndoRelay";

    [NonPersistent]
    public readonly SyncList<ScriptUndoOffer> Offers = new();
    [NonPersistent]
    public readonly SyncList<ScriptUndoRequest> Requests = new();

    // On the owner's machine: the batches in its history by number, what it last asked of each, and the
    // numbers it has taken. What it asked is written again whenever the entry reads otherwise, so a write by
    // anyone else, which the host ignores, never stands in for the owner's own.
    private readonly Dictionary<int, ScriptUndoProxy> _held = new();
    private readonly Dictionary<int, (bool Undone, bool LetGo)> _asked = new();
    private readonly List<ScriptUndoProxy> _gone = new();
    private readonly List<int> _again = new();
    private readonly HashSet<int> _taken = new();
    private bool _closing;

    public override void OnAwake()
    {
        base.OnAwake();
        Offers.MarkHostOnly();
    }

    public override void OnStart()
    {
        base.OnStart();
        if (World?.IsAuthority == true)
            World.Scripts.Undo.Register(this);
    }

    public override void OnDestroy()
    {
        World?.ScriptsIfAny?.Undo.Unregister(this);
        // Nothing carries them any more, so the history drops them. The host lets go of what it offered here
        // on its own once the relay is gone.
        _closing = true;
        if (_held.Count > 0 && ScriptUndoHub.LocalHistory(World) is { } history)
        {
            var held = new List<ScriptUndoProxy>(_held.Values);
            _held.Clear();
            foreach (var proxy in held)
                history.ClearUndoPoints(batch => ReferenceEquals(batch, proxy));
        }
        base.OnDestroy();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (IsOwnersMachine)
            Take();
    }

    private bool IsOwnersMachine
        => World is { IsAuthority: false, LocalUser: { } local } && local.AllocationID.Value == ReferenceID.GetUserByte();

    // New offers into this person's history, what the host has let go of out of it, and requests the host is
    // done with cleared away.
    private void Take()
    {
        var history = ScriptUndoHub.LocalHistory(World);
        for (int i = 0; i < Offers.Count; i++)
        {
            var offer = Offers[i];
            int id = offer.Id.Value;
            if (!_taken.Add(id))
                continue;
            if (offer.Dropped.Value || history == null)
            {
                Write(id, undone: false, letGo: true);
                continue;
            }
            var proxy = new ScriptUndoProxy(this, id, offer.Description.Value ?? string.Empty);
            _held[id] = proxy;
            Write(id, undone: false, letGo: false);
            history.Record(proxy);
        }

        _gone.Clear();
        foreach (var (id, proxy) in _held)
        {
            if (FindOffer(id) is not { Dropped.Value: false })
                _gone.Add(proxy);
        }
        foreach (var proxy in _gone)
        {
            if (history == null || history.ClearUndoPoints(batch => ReferenceEquals(batch, proxy)) == 0)
                LetGo(proxy.Id);
        }

        for (int i = Requests.Count - 1; i >= 0; i--)
        {
            var request = Requests[i];
            int id = request.Id.Value;
            if (!_taken.Contains(id) || _held.ContainsKey(id) || FindOffer(id) != null || !IsMine(request))
                continue;
            _asked.Remove(id);
            _taken.Remove(id);
            using (World.DataModelPermissions?.EnterActor(World.LocalUser))
                Requests.RemoveAt(i);
        }

        _again.Clear();
        foreach (var (id, asked) in _asked)
        {
            if (FindOffer(id) == null)
                continue;
            if (FindRequest(id) is not { } request || request.Undone.Value != asked.Undone || request.LetGo.Value != asked.LetGo)
                _again.Add(id);
        }
        foreach (int id in _again)
            Write(id, _asked[id].Undone, _asked[id].LetGo);
    }

    // From the owner's history: undo or redo one batch. False when the host has let it go or its author has
    // left; the history then drops it.
    internal bool Ask(int id, bool undone)
    {
        if (IsDestroyed || !IsOwnersMachine || FindOffer(id) is not { Dropped.Value: false } offer || offer.AuthorGone)
            return false;
        Write(id, undone, letGo: false);
        return true;
    }

    internal void LetGo(int id)
    {
        _held.Remove(id);
        if (_closing || IsDestroyed || !IsOwnersMachine)
            return;
        Write(id, FindRequest(id)?.Undone.Value ?? false, letGo: true);
    }

    // Written as the person at this machine, whatever scope their history was worked in.
    private void Write(int id, bool undone, bool letGo)
    {
        _asked[id] = (undone, letGo);
        using (World.DataModelPermissions?.EnterActor(World.LocalUser))
        {
            var request = FindRequest(id);
            if (request == null)
            {
                request = Requests.Add();
                request.Id.Value = id;
            }
            if (request.Undone.Value != undone)
                request.Undone.Value = undone;
            if (request.LetGo.Value != letGo)
                request.LetGo.Value = letGo;
        }
    }

    internal ScriptUndoOffer? FindOffer(int id)
    {
        foreach (var offer in Offers)
        {
            if (offer.Id.Value == id)
                return offer;
        }
        return null;
    }

    // The owner's own entry for a batch, never one someone else put in the list.
    private ScriptUndoRequest? FindRequest(int id)
    {
        foreach (var request in Requests)
        {
            if (request.Id.Value == id && IsMine(request))
                return request;
        }
        return null;
    }

    private bool IsMine(ScriptUndoRequest request) => request.ReferenceID.GetUserByte() == ReferenceID.GetUserByte();

    // The person at this machine's relay in this world, made on first use.
    internal static ScriptUndoRelay? For(World world)
    {
        var local = world.LocalUser;
        var root = local?.Root?.Slot;
        if (local == null || root == null || root.IsDestroyed)
            return null;
        var existing = root.FindChild(RelaySlotName, false)?.GetComponent<ScriptUndoRelay>();
        if (existing != null && !existing.IsDestroyed && existing.ReferenceID.GetUserByte() == local.AllocationID.Value)
            return existing;
        if (!world.IsLocalAllocationReady)
            return null;
        var slot = world.AddLocalUserSlot(root, RelaySlotName);
        slot.Persistent.Value = false;
        using (world.EnterLocalUserAllocation())
            return slot.AttachComponent<ScriptUndoRelay>();
    }
}
