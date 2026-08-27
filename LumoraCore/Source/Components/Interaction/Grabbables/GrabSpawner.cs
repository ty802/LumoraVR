// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// A dispenser you pull things out of. Gripping it does not move it: it stamps out a copy of
// GrabSpawnerBase.Template and puts THAT in the hand, so the shelf stays where it is and the user
// walks away with a fresh one.
//
// The dispenser slot needs a collider for the laser or the hand to find it, the same as any other
// grabbable. Set a limit with GrabSpawnerBase.MaxInstances and let go near the shelf to put a copy
// back.
//
// It is an IGrabbable that is never grabbed. The grabber asks it to be picked up, it answers with a
// different object, and the grabber holds that instead - which works because Grab's RETURN VALUE is
// what a hand records, not the thing it aimed at. Everything else falls out of that: the dispenser
// reports itself as not held, not scalable and not receivable, because none of those are ever true
// of something nobody is holding.
//
// The whole decision runs on the one peer that gripped it. The copy and its placement replicate as
// ordinary hierarchy edits, so the other peers see a new object appear in somebody's hand and never
// have to agree about anything. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public class GrabSpawner : GrabSpawnerBase, IGrabbable, ICustomInspectorUI
{
    // Only a hand physically reaching the dispenser may pull from it, never the laser.
    public readonly Sync<bool> PhysicalOnly;

    // Which grabbable wins when several overlap the same grip.
    public readonly Sync<int> GrabPriority;

    // Which interaction target wins when several overlap the same pointer.
    public readonly Sync<int> InteractionPriority;

    // Put the copy in the hand rather than at the dispenser, for a shelf you reach into.
    public readonly Sync<bool> SpawnAtHand;

    public GrabSpawner()
    {
        PhysicalOnly = new Sync<bool>(this, false);
        GrabPriority = new Sync<int>(this, 0);
        InteractionPriority = new Sync<int>(this, 0);
        SpawnAtHand = new Sync<bool>(this, false);
    }

    // A dispenser is never in a hand: it hands out copies and stays put.
    public bool IsGrabbed => false;
    public bool Scalable => false;
    public bool Receivable => false;
    public bool CanBeStolen => false;
    public Grabber? Grabber => null;

    bool IGrabbable.AllowOnlyPhysicalGrab => PhysicalOnly.Value;
    int IGrabbable.GrabPriority => GrabPriority.Value;
    public int InteractionTargetPriority => InteractionPriority.Value;

    // Raised on the puller's peer each time a copy is dispensed.
    public event Action<IGrabbable>? OnLocalGrabbed;

    // Never raised. A dispenser has nothing to let go of.
    // Empty accessors rather than a field-like event: a subscriber list that nothing can ever raise
    // is a leak with a nice name on it.
    event Action<IGrabbable>? IGrabbable.OnLocalReleased { add { } remove { } }

    public bool CanGrab(Grabber grabber) => grabber != null && CanSpawn;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        bool ready = CanSpawn;
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = ready ? LaserCursor.Grab : LaserCursor.Disabled,
            ForceActivate = false,
        };
    }

    public IGrabbable Grab(Grabber grabber, Slot holdSlot, bool suppressEvents = false)
    {
        // Returning ourselves is the refusal: the grabber checks whether what came back is actually
        // in its hand, and we are never in anyone's hand, so it records nothing and the grip ends.
        if (!CanGrab(grabber) || holdSlot == null || holdSlot.IsDestroyed)
            return this;

        var pose = SpawnAtHand.Value ? holdSlot : Slot;
        float3 position = pose?.GlobalPosition ?? float3.Zero;
        floatQ rotation = pose?.GlobalRotation ?? floatQ.Identity;

        var spawned = Spawn(in position, in rotation, overridePose: true);
        if (spawned == null)
            return this;

        if (!suppressEvents)
            OnLocalGrabbed?.Invoke(this);

        return spawned.Grab(grabber, holdSlot, suppressEvents);
    }

    public void Release(Grabber grabber, bool suppressEvents = false)
    {
        // Nothing to do, and deliberately not an error. A hand never records the dispenser as held,
        // so this can only arrive from a caller letting go of everything it thinks it has, and
        // throwing out of the middle of somebody's release would take the rest of it down.
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Template", Template.Target?.SlotName.Value ?? "none");
        int max = MaxInstances.Value;
        InspectorStats.AddRow(ui, "Live copies", max > 0 ? $"{LiveInstances} / {max}" : LiveInstances.ToString());
        InspectorStats.AddRow(ui, "Goes to", SpawnParent.Target?.SlotName.Value ?? "world root");
        InspectorStats.AddRow(ui, "State", DescribeBlock() ?? "ready");
    }
}
