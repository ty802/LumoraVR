// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Inactive non-persistent holding pen for slots whose destruction is undoable. Parking instead of
// destroying means undo needs no serialization round-trip; parked slots are destroyed for real when
// their step leaves the history.
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

    public static Slot? Find(World? world)
    {
        var graveyard = world?.RootSlot?.FindChild(SlotName, recursive: false);
        return graveyard == null || graveyard.IsDestroyed ? null : graveyard;
    }

    // Called when a manager tears down. The pen is shared, so it only goes when the last parked slot
    // has gone with it - and only the authority swings the axe, or every peer that saw the same user
    // leave would send its own destroy for the same slot.
    public static void DisposeIfEmpty(World? world)
    {
        if (world == null || !world.IsAuthority)
            return;
        var graveyard = Find(world);
        if (graveyard != null && graveyard.Children.Count == 0 && graveyard.Components.Count == 0)
            graveyard.Destroy();
    }
}

// Reversible existence change for a set of slots: Destroy (park on record, undo restores) and
// Duplicate/Create (undo parks, redo restores) are the two directions of the same operation.
//
// Restoring is refused outright when the slot's original parent has been destroyed since. The old
// behaviour was to fall back to the world root, which quietly resurrected a piece of a deleted
// hierarchy at the world origin and reported success; failing instead lets the manager drop the step
// and the eviction finishes the destroy the user asked for. -xlinka
public sealed class SlotExistenceUndoBatch : IUndoBatch, IUndoTargetQuery
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
    private readonly bool _isDestroy;
    private bool _parked;

    public LocaleText LocalizedDescription { get; }

    public string Description => LocalizedDescription.Resolve();

    private SlotExistenceUndoBatch(World world, LocaleText description, bool isDestroy)
    {
        _world = world;
        LocalizedDescription = description;
        _isDestroy = isDestroy;
    }

    // Park the slots now (undoable destroy). Null if nothing could be parked.
    public static SlotExistenceUndoBatch? Destroy(World? world, IEnumerable<Slot> slots)
    {
        var batch = Create(world, slots, UndoLocale.Destroy, isDestroy: true);
        if (batch == null)
            return null;
        return batch.Park() ? batch : null;
    }

    // Track freshly created slots (undoable duplicate/spawn).
    public static SlotExistenceUndoBatch? Created(World? world, IEnumerable<Slot> slots, LocaleText description)
        => Create(world, slots, description, isDestroy: false);

    private static SlotExistenceUndoBatch? Create(World? world, IEnumerable<Slot> slots, LocaleText description, bool isDestroy)
    {
        if (world == null)
            return null;

        var batch = new SlotExistenceUndoBatch(world, description, isDestroy);
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

    public bool Undo() => _isDestroy ? Restore() : Park();

    public bool Redo() => _isDestroy ? Park() : Restore();

    public bool ReferencesElement(IWorldElement element)
    {
        if (element is not Slot slot)
            return false;
        foreach (var entry in _entries)
        {
            if (UndoTargets.Touches(entry.Slot, slot) || UndoTargets.Touches(entry.OriginalParent, slot))
                return true;
        }
        return false;
    }

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
        // All or nothing on the destination: a parent that has since been destroyed means this step
        // has nowhere valid to put its slots back.
        foreach (var entry in _entries)
        {
            if (!entry.Slot.IsDestroyed && !UndoTargets.CanRestoreUnder(entry.OriginalParent))
                return false;
        }

        bool any = false;
        foreach (var entry in _entries)
        {
            if (entry.Slot.IsDestroyed)
                continue;

            var parent = entry.OriginalParent ?? _world.RootSlot;
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
        _parked = false;
    }
}

// Reversible user scale change (Reset Scale).
public sealed class UserScaleUndoBatch : IUndoBatch
{
    private readonly UserRoot _userRoot;
    private readonly float _before;
    private readonly float _after;

    public LocaleText LocalizedDescription => UndoLocale.Scale;

    public string Description => LocalizedDescription.Resolve();

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

// Shared rules for "can this step still put something back, and does it touch that slot".
internal static class UndoTargets
{
    // A null parent means the slot sat at the top of the world, which stays restorable.
    public static bool CanRestoreUnder(Slot? parent) => parent == null || !parent.IsDestroyed;

    public static bool Touches(Slot? candidate, Slot subtreeRoot)
    {
        if (candidate == null)
            return false;
        return ReferenceEquals(candidate, subtreeRoot) || candidate.IsDescendantOf(subtreeRoot);
    }
}
