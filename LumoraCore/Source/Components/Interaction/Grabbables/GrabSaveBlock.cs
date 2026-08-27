// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;

namespace Lumora.Core.Components.Interaction;

// Keeps the object it sits on out of saves: never while it is in someone's hand, and optionally
// never at all. Also refuses the held-object menu's save-to-inventory route, for game pieces that
// are meant to be played with rather than pocketed.
//
// Not a security measure. An inspector or a tool can clear the flag or copy the object out from
// under it; the point is to stop the ordinary routes, not to make the object uncopyable.
//
// The world save filter walks the hierarchy and keeps whatever reports itself persistent, which on
// a slot is just its Persistent field. So blocking a save means clearing that field, not
// intercepting the writer - there is no per-slot save callback to hook, and adding one would put a
// virtual call on every slot in the world to serve a handful of props.
//
// While-held is the case that actually bites. A grabbed object hangs under the hand's holder slot,
// so a save taken mid-grab either captures it parented to a piece of somebody's rig or loses it
// because the rig is not saved either. Neither is a state worth reloading, so the object steps out
// of the save for the length of the grab and steps back in on release.
//
// Everything runs through one suppress/restore pair with a record of what the flag was BEFORE we
// touched it, so a slot the author had already marked unsaveable is not quietly turned back on when
// this component is disabled or the grip ends. We only ever undo our own change.
//
// Persistent is itself a non-persisted field, so a loaded slot always comes back saveable no matter
// what it was when the save was written. That is why the always-on case re-asserts from start and
// from the slot's own change event rather than being set once at attach. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public class GrabSaveBlock : GrabEventBehaviour, ICustomInspectorUI
{
    // Step out of world saves while the object is being carried.
    public readonly Sync<bool> BlockWhileHeld;

    // Step out of world saves permanently, carried or not.
    public readonly Sync<bool> BlockWorldSave;

    // Refuse the held-object menu's save-to-inventory action.
    public readonly Sync<bool> BlockInventorySave;

    private bool _suppressed;
    private bool _restoreValue = true;
    private bool _hookedSlot;
    private bool _writeFailureLogged;

    public GrabSaveBlock()
    {
        BlockWhileHeld = new Sync<bool>(this, true);
        BlockWorldSave = new Sync<bool>(this, false);
        BlockInventorySave = new Sync<bool>(this, true);
    }

    // True when slot or anything above it refuses to be filed away.
    public static bool BlocksInventorySave(Slot? slot)
    {
        for (var current = slot; current != null && !current.IsDestroyed; current = current.Parent)
        {
            foreach (var block in current.GetComponents<GrabSaveBlock>())
            {
                if (!block.IsDestroyed && block.Enabled.Value && block.BlockInventorySave.Value)
                    return true;
            }
        }
        return false;
    }

    public bool IsSuppressing => _suppressed;

    public override void OnStart()
    {
        base.OnStart();
        var slot = Slot;
        if (slot != null && !_hookedSlot)
        {
            slot.PersistentChanged += OnSlotPersistentChanged;
            _hookedSlot = true;
        }
        BlockWhileHeld.OnValueChange += _ => Apply();
        BlockWorldSave.OnValueChange += _ => Apply();
        Apply();
    }

    public override void OnDestroy()
    {
        var slot = Slot;
        if (_hookedSlot && slot != null)
            slot.PersistentChanged -= OnSlotPersistentChanged;
        _hookedSlot = false;
        // Leave the world in the state it would have been in without us.
        Restore();
        base.OnDestroy();
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        Apply();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        Apply();
    }

    protected override void OnGrabbed(Grabbable carrier) => Apply();

    protected override void OnReleased(Grabbable carrier) => Apply();

    private bool ShouldSuppress
    {
        get
        {
            if (IsDestroyed || !Enabled.Value)
                return false;
            if (BlockWorldSave.Value)
                return true;
            return BlockWhileHeld.Value && IsHeldLocally;
        }
    }

    private void Apply()
    {
        if (ShouldSuppress)
            Suppress();
        else
            Restore();
    }

    private void Suppress()
    {
        var slot = Slot;
        if (_suppressed || slot == null || slot.IsDestroyed)
            return;
        // A protected slot is forced saveable by whatever protected it, and fighting that would
        // only produce a write the next MarkProtected undoes.
        if (slot.ForcedPersistent)
            return;

        _restoreValue = slot.Persistent.Value;
        _suppressed = true;
        if (_restoreValue)
            Write(slot, false);
    }

    private void Restore()
    {
        var slot = Slot;
        if (!_suppressed)
            return;
        _suppressed = false;
        if (slot == null || slot.IsDestroyed || slot.ForcedPersistent)
            return;
        if (slot.Persistent.Value != _restoreValue)
            Write(slot, _restoreValue);
    }

    // The peer running this is the one holding the object, which does not necessarily mean it may
    // edit the slot's own fields - a grab is permitted through the grab surface, and this is not on
    // it. A refusal is a normal outcome in somebody else's content, so it is reported once and
    // dropped rather than thrown into the middle of a release.
    private void Write(Slot slot, bool value)
    {
        try
        {
            slot.Persistent.Value = value;
        }
        catch (Exception ex)
        {
            if (_writeFailureLogged)
                return;
            _writeFailureLogged = true;
            Logging.Logger.Warn($"GrabSaveBlock on {ParentHierarchyToString()} could not set Persistent: {ex.Message}");
        }
    }

    // Only reacts to the flag coming back ON, so re-clearing it cannot loop through this handler.
    private void OnSlotPersistentChanged(Slot slot)
    {
        if (slot == null || !slot.Persistent.Value || !_suppressed)
            return;
        // Something turned it back on while we were meant to be holding it off. Take the new value
        // as the one to restore later and put it back down.
        _restoreValue = true;
        Write(slot, false);
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        var slot = Slot;
        string state = slot == null || slot.IsDestroyed
            ? "no slot"
            : slot.ForcedPersistent ? "protected slot, cannot block"
            : slot.Persistent.Value ? "in saves" : "out of saves";
        InspectorStats.AddRow(ui, "Slot", state);
        InspectorStats.AddRow(ui, "Reason",
            !_suppressed ? "not blocking" : BlockWorldSave.Value ? "always" : "held");
        InspectorStats.AddRow(ui, "Inventory", BlockInventorySave.Value ? "refused" : "allowed");
        InspectorStats.AddRow(ui, "Carrier", Carrier?.Slot?.SlotName.Value ?? "none");
    }
}
