// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Lumora.Warden;

namespace Lumora.Core.Components;

// implements IGrabbable so any Grabber can pick this up. The holder is stored as a replicated
// SyncRef, so EVERY peer can see who's holding this object just by resolving the reference - there's
// no separate "who holds it" message and no list to sync. The host is the authority on that ref, so
// when two users reach for the same thing the host's accepted write is what everyone ends up seeing,
// and the loser drops it on their next check. parent under the grabber's holder slot on grab; restore
// on release. - xlinka
// The one member every grab surface has: the reference that records who holds it. The authority's
// in-batch claim check reads it through this seam so props and bone chains answer the same way.
public interface IGrabHolderSurface
{
    RefID GrabHolderRefId { get; }
}

[ComponentCategory("Interaction")]
public sealed class Grabbable : Component, IGrabbable, IPermissionGrabSurface, IGrabHolderSurface
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

    // THROW TUNABLES
    // KeepMomentum off is the old behaviour: let go and it stops dead. ThrowMultiplier scales the linear AND
    // the spin together, so a prop can be tuned to leave the hand faster than the hand actually moved.
    // MaxThrowSpeed is in m/s and is applied by whichever peer simulates the body, not by the sender. - xlinka
    public readonly Sync<bool> KeepMomentum = new();
    public readonly Sync<float> ThrowMultiplier = new();
    public readonly Sync<float> MaxThrowSpeed = new();

    // The hand-off itself, in world units. The releasing hand writes these as part of the grab protocol (so a
    // guest can throw a prop it doesn't own), and every peer reads them when the holder clears - only the one
    // that simulates the body acts on them. Not persisted: a saved world has no throw in flight. - xlinka
    [NonPersistent]
    public readonly Sync<float3> ReleaseLinearVelocity = new();

    [NonPersistent]
    public readonly Sync<float3> ReleaseAngularVelocity = new();

    // The replicated holder. Null target == not held. All peers read who holds this from here. - xlinka
    public readonly SyncRef<Grabber> GrabberRef = new();

    // Where the object came from before it was grabbed, replicated so ANY peer (or the host when the
    // holder disconnects) can put it back - not just the one machine that grabbed it. Null when not held. - xlinka
    public readonly SyncRef<Slot> LastParentRef = new();

    // The last holder we saw locally, so we can spot a steal (the ref changing out from under us). Local. - xlinka
    private Grabber? _lastKnownHolder;

    // Whether the release about to happen came through a hand that measured a throw. Local to the releaser. - xlinka
    private bool _momentumStaged;

    public bool IsGrabbed => GrabberRef.Target != null;
    public Grabber? Grabber => GrabberRef.Target;

    // PERMISSION GATE VIEW
    // A grab is an interaction, not an ownership edit, so the gate needs to tell a write to the grab
    // protocol apart from an edit of the object. Only the two replicated grab refs count; everything
    // else on this component stays owner-gated. -xlinka

    bool IPermissionGrabSurface.AllowsGrab => AllowGrab.Value;

    bool IPermissionGrabSurface.AllowsSteal => AllowSteal.Value;

    IPermissionActor? IPermissionGrabSurface.CurrentHolder => GrabberRef.Target?.OwningUser;

    RefID IGrabHolderSurface.GrabHolderRefId => GrabberRef.ReferenceID;

    GrabWriteKind IPermissionGrabSurface.ClassifyGrabWrite(IPermissionTarget? member)
    {
        if (ReferenceEquals(member, GrabberRef))
            return GrabWriteKind.HolderRef;
        if (ReferenceEquals(member, LastParentRef)
            || ReferenceEquals(member, ReleaseLinearVelocity)
            || ReferenceEquals(member, ReleaseAngularVelocity))
            return GrabWriteKind.GrabState;
        return GrabWriteKind.None;
    }

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
        KeepMomentum.Value = true;
        ThrowMultiplier.Value = 1f;
        // Matches the hard cap the physics side enforces on any live body, so the tunable is the one that
        // actually decides the throw until someone lowers it. - xlinka
        MaxThrowSpeed.Value = 8f;
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

        // A release that didn't come through a hand (disable, steal cleanup, teardown) carries no throw. Wipe
        // the staged values or every peer replays the last one. - xlinka
        if (!_momentumStaged) WriteReleaseMomentum(float3.Zero, float3.Zero);
        _momentumStaged = false;

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

        // Let go. Deferred one update on purpose: the release arrives as a delta batch and there is no
        // guarantee the velocity fields decode before the holder ref that triggers this. One update later
        // the whole batch has landed, on every peer, including the one that did the releasing. - xlinka
        if (oldHolder != null && newHolder == null)
        {
            RunInUpdates(1, ApplyReleaseMomentum);
        }

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

    // THROW HAND-OFF
    // The measuring happens in the hand (Grabber keeps a rolling window of the held object's world pose).
    // This end applies the tunables, publishes the result, and - one update later, on every peer - feeds it
    // to the body. Only the peer that simulates the body acts; everyone else's write is a no-op. - xlinka

    internal void StageReleaseMomentum(float3 linear, float3 angular)
    {
        if (!KeepMomentum.Value)
        {
            linear = float3.Zero;
            angular = float3.Zero;
        }
        else
        {
            float multiplier = ThrowMultiplier.Value;
            linear *= multiplier;
            angular *= multiplier;
        }

        WriteReleaseMomentum(linear, angular);
        _momentumStaged = true;
    }

    private void WriteReleaseMomentum(float3 linear, float3 angular)
    {
        if (ReleaseLinearVelocity.Value == linear && ReleaseAngularVelocity.Value == angular) return;

        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            ReleaseLinearVelocity.Value = linear;
            ReleaseAngularVelocity.Value = angular;
        }
    }

    private void ApplyReleaseMomentum()
    {
        if (IsDestroyed || IsGrabbed || !KeepMomentum.Value) return;

        var linear = ReleaseLinearVelocity.Value;
        var angular = ReleaseAngularVelocity.Value;
        if (linear == float3.Zero && angular == float3.Zero) return;

        var body = FindMomentumBody();
        if (body == null || body.IsDestroyed || !body.Enabled.Value || body.IsKinematic.Value) return;

        // Clamped HERE, by the peer that owns the simulation, not by the sender. A peer that publishes a
        // silly number gets it cut down on arrival, and the physics side still caps anything that gets past
        // a generous MaxThrowSpeed. - xlinka
        float max = MaxThrowSpeed.Value;
        if (max > 0f && linear.LengthSquared > max * max) linear = linear.Normalized * max;

        body.SetVelocities(linear, angular);
    }

    // The body normally sits on the grabbable's own slot; a compound prop can put the Grabbable on the root
    // and the body on the part carrying the collider. Deliberately never looks upward: by the time this runs
    // the object is back under its restore parent, so up is the rest of the world. - xlinka
    private RigidBody? FindMomentumBody()
    {
        var slot = Slot;
        if (slot == null || slot.IsRemoved) return null;
        return slot.GetComponent<RigidBody>() ?? slot.GetComponentInChildren<RigidBody>(includeSelf: false);
    }
}
