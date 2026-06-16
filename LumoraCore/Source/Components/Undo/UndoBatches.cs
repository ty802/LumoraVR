// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Inactive non-persistent holding pen for slots whose destruction is undoable.
/// Parking instead of destroying means undo needs no serialization round-trip;
/// parked slots are destroyed for real when their batch leaves the history.
/// </summary>
public static class UndoGraveyard
{
    private const string SlotName = "UndoGraveyard";

    public static Slot? Acquire(World? world)
    {
        var root = world?.RootSlot;
        if (root == null)
            return null;

        var graveyard = root.FindChild(SlotName, recursive: false);
        if (graveyard == null || graveyard.IsDestroyed)
        {
            graveyard = root.AddSlot(SlotName);
            graveyard.Persistent.Value = false;
            graveyard.ActiveSelf.Value = false;
        }
        return graveyard;
    }
}

/// <summary>
/// Reversible existence change for a set of slots: Destroy (park on record, undo
/// restores) and Duplicate/Create (undo parks, redo restores) are the two
/// directions of the same operation.
/// </summary>
public sealed class SlotExistenceUndoBatch : IUndoBatch
{
    private sealed class Entry
    {
        public Slot Slot = null!;
        public Slot? OriginalParent;
        public float3 LocalPosition;
        public floatQ LocalRotation;
        public float3 LocalScale;
        public bool WasActive;
    }

    private readonly List<Entry> _entries = new();
    private readonly World _world;
    private bool _parked;

    public string Description { get; }

    private SlotExistenceUndoBatch(World world, string description)
    {
        _world = world;
        Description = description;
    }

    /// <summary>Park the slots now (undoable destroy). Null if nothing could be parked.</summary>
    public static SlotExistenceUndoBatch? Destroy(World? world, IEnumerable<Slot> slots)
    {
        var batch = Create(world, slots, "Destroy");
        if (batch == null)
            return null;
        if (!batch.Park())
            return null;
        return batch;
    }

    /// <summary>Track freshly created slots (undoable duplicate/spawn).</summary>
    public static SlotExistenceUndoBatch? Created(World? world, IEnumerable<Slot> slots, string description)
    {
        return Create(world, slots, description);
    }

    private static SlotExistenceUndoBatch? Create(World? world, IEnumerable<Slot> slots, string description)
    {
        if (world == null)
            return null;

        var batch = new SlotExistenceUndoBatch(world, description);
        foreach (var slot in slots)
        {
            if (slot == null || slot.IsDestroyed)
                continue;
            batch._entries.Add(new Entry
            {
                Slot = slot,
                OriginalParent = slot.Parent,
                LocalPosition = slot.LocalPosition.Value,
                LocalRotation = slot.LocalRotation.Value,
                LocalScale = slot.LocalScale.Value,
                WasActive = slot.ActiveSelf.Value,
            });
        }
        return batch._entries.Count > 0 ? batch : null;
    }

    public bool Undo() => Description == "Destroy" ? Restore() : Park();

    public bool Redo() => Description == "Destroy" ? Park() : Restore();

    private bool Park()
    {
        var graveyard = UndoGraveyard.Acquire(_world);
        if (graveyard == null)
            return false;

        bool any = false;
        foreach (var entry in _entries)
        {
            if (entry.Slot.IsDestroyed)
                continue;
            entry.Slot.SetParent(graveyard);
            entry.Slot.ActiveSelf.Value = false;
            any = true;
        }
        _parked = any;
        return any;
    }

    private bool Restore()
    {
        bool any = false;
        foreach (var entry in _entries)
        {
            if (entry.Slot.IsDestroyed)
                continue;

            var parent = entry.OriginalParent != null && !entry.OriginalParent.IsDestroyed
                ? entry.OriginalParent
                : _world.RootSlot;
            if (parent == null)
                continue;

            entry.Slot.SetParent(parent);
            entry.Slot.LocalPosition.Value = entry.LocalPosition;
            entry.Slot.LocalRotation.Value = entry.LocalRotation;
            entry.Slot.LocalScale.Value = entry.LocalScale;
            entry.Slot.ActiveSelf.Value = entry.WasActive;
            any = true;
        }
        if (any)
            _parked = false;
        return any;
    }

    public void OnEvicted()
    {
        if (!_parked)
            return;
        foreach (var entry in _entries)
        {
            if (!entry.Slot.IsDestroyed)
                entry.Slot.Destroy();
        }
    }
}

/// <summary>Reversible user scale change (Reset Scale).</summary>
public sealed class UserScaleUndoBatch : IUndoBatch
{
    private readonly UserRoot _userRoot;
    private readonly float _before;
    private readonly float _after;

    public string Description => "Scale";

    public UserScaleUndoBatch(UserRoot userRoot, float before, float after)
    {
        _userRoot = userRoot;
        _before = before;
        _after = after;
    }

    public bool Undo() => Apply(_before);

    public bool Redo() => Apply(_after);

    private bool Apply(float scale)
    {
        if (_userRoot == null || _userRoot.IsDestroyed)
            return false;
        _userRoot.GlobalScale = scale;
        return true;
    }

    public void OnEvicted()
    {
    }
}
