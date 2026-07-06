// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components;

// implements IGrabbable so any Grabber can pick this up. The holder is stored as a replicated
// SyncRef, so EVERY peer can see who's holding this object just by resolving the reference - there's
// no separate "who holds it" message and no list to sync. The host is the authority on that ref, so
// when two users reach for the same thing the host's accepted write is what everyone ends up seeing,
// and the loser drops it on their next check. parent under the grabber's holder slot on grab; restore
// on release. - xlinka
[ComponentCategory("Interaction")]
public sealed class Grabbable : Component, IGrabbable
{
    public readonly Sync<bool> AllowGrab = new();
    public readonly Sync<bool> FollowRotation = new();
    public readonly Sync<bool> Scalable = new();
    public readonly Sync<bool> Receivable = new();
    public readonly Sync<bool> AllowOnlyPhysicalGrab = new();
    public readonly Sync<int> GrabPriority = new();
    public readonly Sync<int> InteractionPriority = new();

    // Whether someone may take this out of another user's hands. Default false: held means held until
    // the holder lets go. Flip true for shared props you want to pass around freely. - xlinka
    public readonly Sync<bool> AllowSteal = new();

    // When this gets disabled while held, let go instead of staying stuck under the hand. - xlinka
    public readonly Sync<bool> DropOnDisable = new();

    // Refuse grabbing once this is part of a worn avatar (parented under a live user root). An unworn
    // avatar object can be picked up off the ground / passed around, but you can't yank someone's worn
    // avatar: no grabbing into a live user hierarchy. - xlinka
    public readonly Sync<bool> BlockWhenWorn = new();

    // The replicated holder. Null target == not held. All peers read who holds this from here. - xlinka
    public readonly SyncRef<Grabber> GrabberRef = new();

    // Where the object came from before it was grabbed, replicated so ANY peer (or the host when the
    // holder disconnects) can put it back - not just the one machine that grabbed it. Null when not held. - xlinka
    public readonly SyncRef<Slot> LastParentRef = new();

    // The last holder we saw locally, so we can spot a steal (the ref changing out from under us). Local. - xlinka
    private Grabber? _lastKnownHolder;

    public bool IsGrabbed => GrabberRef.Target != null;
    public Grabber? Grabber => GrabberRef.Target;

    bool IGrabbable.Scalable => Scalable.Value;
    bool IGrabbable.Receivable => Receivable.Value;
    bool IGrabbable.AllowOnlyPhysicalGrab => AllowOnlyPhysicalGrab.Value;
    int IGrabbable.GrabPriority => GrabPriority.Value;
    bool IGrabbable.CanBeStolen => AllowSteal.Value;

    public int InteractionTargetPriority => InteractionPriority.Value;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        // Only offer the grab cursor when we could actually grab it: grabbing is allowed AND it's
        // either free or stealable. A locked-in held object reads as disabled so you don't try. - xlinka
        bool grabbable = AllowGrab.Value && (!IsGrabbed || AllowSteal.Value)
            && !(BlockWhenWorn.Value && Slot?.ActiveUserRoot != null);
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = grabbable ? LaserCursor.Grab : LaserCursor.Disabled,
            ForceActivate = false,
        };
    }

    public event Action<IGrabbable>? OnLocalGrabbed;
    public event Action<IGrabbable>? OnLocalReleased;

    public override void OnAwake()
    {
        base.OnAwake();
        // Runs on every instance (including ones decoded from the network), unlike OnInit. Watch the
        // holder ref so a steal the host hands to someone else makes us drop it locally. - xlinka
        GrabberRef.OnTargetChange += OnHolderChanged;
    }

    public override void OnInit()
    {
        base.OnInit();
        AllowGrab.Value = true;
        FollowRotation.Value = false;
        Scalable.Value = true;
        Receivable.Value = true;
        AllowOnlyPhysicalGrab.Value = false;
        GrabPriority.Value = 0;
        InteractionPriority.Value = 0;
        AllowSteal.Value = false;
        DropOnDisable.Value = true;
        BlockWhenWorn.Value = false;
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        // If we're held when we get disabled, let go so we reparent to the restore parent instead of
        // dangling under the hand's holder slot. Capture the holder first since Release nulls it. -xlinka
        var holder = GrabberRef.Target;
        if (holder != null && DropOnDisable.Value)
        {
            Release(holder);
        }
    }

    public bool CanGrab(Grabber grabber)
    {
        if (IsDestroyed || !AllowGrab.Value) return false;

        // Worn avatar/equipment: parented under a live user root - not grabbable.
        if (BlockWhenWorn.Value && Slot?.ActiveUserRoot != null) return false;

        // A driven transform is owned by whatever drives it. Grabbing would just fight the driver every
        // frame - reparent, the driver overwrites the local pose, the object snaps back. So don't offer
        // the grab while position/rotation/scale is being driven. -xlinka
        var slot = Slot;
        if (slot != null && (slot.LocalPosition.IsDriven || slot.LocalRotation.IsDriven || slot.LocalScale.IsDriven))
            return false;

        var current = GrabberRef.Target;
        if (current != null)
        {
            // Already held. You can only take it if the owner allowed stealing, you're not already
            // the holder, and you're not trying to steal from your own other hand. - xlinka
            if (!AllowSteal.Value) return false;
            if (ReferenceEquals(current, grabber)) return false;
            var holdingUser = current.OwningUser;
            if (holdingUser != null && ReferenceEquals(holdingUser, World?.LocalUser)) return false;
        }
        return true;
    }

    public IGrabbable Grab(Grabber grabber, Slot holdSlot, bool suppressEvents = false)
    {
        if (!CanGrab(grabber)) return this;

        var prior = GrabberRef.Target;
        if (prior != null && !ReferenceEquals(prior, grabber))
            prior.NotifyStolen(this);

        // Commit the holder and the restore-parent. On the host this is authoritative; on a client it's
        // optimistic and the host's accepted value (or a correction) replicates back through the ref. The
        // bypass is so a guest can write the holder/parent of an object it doesn't own - the host still
        // arbitrates. Remember the original parent in the data model (not just locally) so anyone can
        // restore it later. - xlinka
        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            LastParentRef.Target = Slot?.Parent!;
            GrabberRef.Target = grabber;
        }
        _lastKnownHolder = grabber;

        // preserveGlobalTransform: keep world position when grabbed, so the object
        // doesn't snap to the hand origin. - xlinka
        Slot?.SetParent(holdSlot, preserveGlobalTransform: true);

        if (!suppressEvents) OnLocalGrabbed?.Invoke(this);
        return this;
    }

    public void Release(Grabber grabber, bool suppressEvents = false)
    {
        // Only the recorded holder can release. A client that already got stolen from must not be able
        // to yank the object out of the new holder's hands with a stale release. - xlinka
        if (!ReferenceEquals(GrabberRef.Target, grabber)) return;

        var restoreParent = LastParentRef.Target;

        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            // The setter treats null as "clear to RefID.Null"; null! just quiets the non-null annotation. -xlinka
            GrabberRef.Target = null!;
            LastParentRef.Target = null!;
        }
        _lastKnownHolder = null;

        if (restoreParent == null || restoreParent.IsDestroyed || (Slot != null && restoreParent.IsDescendantOf(Slot)))
        {
            restoreParent = World?.RootSlot;
        }
        Slot?.SetParent(restoreParent!, preserveGlobalTransform: true);

        if (!suppressEvents) OnLocalReleased?.Invoke(this);
    }

    private void OnHolderChanged(SyncRef<Grabber> reference)
    {
        // Don't react while the ref is still resolving on world load - that's not a real grab. - xlinka
        if (reference.IsInInitPhase || reference.IsLoading) return;

        var newHolder = reference.Target;
        var oldHolder = _lastKnownHolder;
        _lastKnownHolder = newHolder;

        if (ReferenceEquals(oldHolder, newHolder)) return;

        // The holder flipped to someone else while WE were holding it = the host handed it off (a
        // steal). Drop our local hold and let go so the hand and any holder-driven UI release. A normal
        // release sets the holder to null and is already handled in Release(), so we skip that here to
        // avoid firing the released event twice. - xlinka
        if (oldHolder != null && newHolder != null && IsLocalGrabber(oldHolder))
        {
            oldHolder.NotifyStolen(this);
            OnLocalReleased?.Invoke(this);
        }
    }

    private bool IsLocalGrabber(Grabber grabber)
    {
        var owner = grabber.OwningUser;
        return owner != null && ReferenceEquals(owner, World?.LocalUser);
    }
}
