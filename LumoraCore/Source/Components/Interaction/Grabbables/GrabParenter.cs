// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components.Magnets;

namespace Lumora.Core.Components.Interaction;

// Files the object away when it is let go: whatever hand it came out of and wherever it was
// dropped, it ends up parented under NewParent. Useful for keeping a world tidy - tools that belong
// in a drawer, parts that belong in an assembly - without the object having to be dropped anywhere
// in particular.
//
// Waits two beats after the release before doing anything, and everything about that number is
// deliberate. A magnet decides where a released object lands one beat later, from a pose captured at
// the release, so acting on the same beat is a coin toss over component update order. Aimed beats
// ambient and ambient beats housekeeping: a receiver the user pointed at claims the object
// immediately, a socket claims it a beat later, and this only speaks up when neither did.
//
// A socketed object is left alone outright rather than merely deprioritised. Being in a socket is
// where it now lives, and pulling it out to file it away would undo a placement the user made on
// purpose. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public class GrabParenter : GrabEventBehaviour, ICustomInspectorUI
{
    // Empty parks it at the world root.
    public readonly SyncRef<Slot> NewParent;

    // Leave it alone when something else already claimed it on the way down.
    public readonly Sync<bool> ParentUnderReceiver;

    public readonly Sync<bool> KeepGlobalTransform;

    // Empty moves the carrier's own slot.
    public readonly SyncRef<Slot> Target;

    private Slot? _releaseParent;
    private string _lastOutcome = "idle";

    public GrabParenter()
    {
        NewParent = new SyncRef<Slot>(this);
        ParentUnderReceiver = new Sync<bool>(this, true);
        KeepGlobalTransform = new Sync<bool>(this, true);
        Target = new SyncRef<Slot>(this);
    }

    public Slot? MovedSlot
    {
        get
        {
            var explicitTarget = Target.Target;
            if (explicitTarget != null && !explicitTarget.IsDestroyed)
                return explicitTarget;
            return Carrier?.Slot ?? Slot;
        }
    }

    protected override void OnReleased(Grabbable carrier)
    {
        // The release has already put the object back under its restore parent, so this is where it
        // sits if nothing else touches it.
        _releaseParent = MovedSlot?.Parent;
        RunInUpdates(2, ResolveParenting);
    }

    private void ResolveParenting()
    {
        if (IsDestroyed || !Enabled.Value)
            return;

        var moved = MovedSlot;
        if (moved == null || moved.IsDestroyed)
            return;

        // Picked straight back up. The next release will run its own resolve.
        if (Carrier?.IsGrabbed == true)
        {
            _lastOutcome = "regrabbed";
            return;
        }

        if (MagnetHelper.IsSocketed(moved))
        {
            _lastOutcome = "left in socket";
            return;
        }

        if (ParentUnderReceiver.Value && !ReferenceEquals(moved.Parent, _releaseParent))
        {
            _lastOutcome = $"left under {moved.Parent?.SlotName.Value ?? "world"}";
            return;
        }

        var parent = NewParent.Target;
        if (parent == null || parent.IsDestroyed)
            parent = World?.RootSlot;
        if (parent == null)
            return;

        if (ReferenceEquals(moved.Parent, parent))
        {
            _lastOutcome = "already there";
            return;
        }

        if (!ReparentGuard.CanReparent(moved, parent))
        {
            _lastOutcome = "blocked";
            return;
        }

        var undo = SlotTransformUndoBatch.Begin(moved, $"File {moved.SlotName.Value}");
        moved.SetParent(parent, KeepGlobalTransform.Value);
        InspectorUndo.Record(this, undo?.Commit());
        _lastOutcome = $"filed under {parent.SlotName.Value}";
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Moves", MovedSlot?.SlotName.Value ?? "nothing");
        InspectorStats.AddRow(ui, "Files under", NewParent.Target?.SlotName.Value ?? "world root");
        InspectorStats.AddRow(ui, "Last release", _lastOutcome);
    }
}
