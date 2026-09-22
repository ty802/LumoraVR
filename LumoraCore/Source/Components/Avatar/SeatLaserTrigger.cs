// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Attaches a RayTarget if the slot has none, because a trigger the laser cannot see is just a
// component that never fires. It does NOT own the seating rules - the press is forwarded straight to
// Seat.TrySit / Seat.Release, which are the same calls a touch trigger or a script would make. -xlinka
[ComponentCategory("Users/Avatar")]
public class SeatLaserTrigger : Component, ISeatTrigger
{
    // falls back to a Seat on this slot
    public readonly SyncRef<Seat> TargetSeat;

    public readonly Sync<bool> AllowSit;

    public readonly Sync<bool> AllowRelease;

    public SeatLaserTrigger()
    {
        TargetSeat = new SyncRef<Seat>(this);
        AllowSit = new Sync<bool>(this, true);
        AllowRelease = new Sync<bool>(this, true);
    }

    private RayTarget _rayTarget = null!;

    public Seat? Seat
    {
        get
        {
            var seat = TargetSeat.Target;
            if (seat != null && !seat.IsDestroyed)
                return seat;
            seat = Slot?.GetComponent<Seat>();
            return seat != null && !seat.IsDestroyed ? seat : null;
        }
    }

    // What this trigger drives. A default interface member is only reachable through the interface,
    // and everything in here talks to the anchor rather than the chair, so it is named on the class.
    public IAvatarAnchor? Anchor => Seat;

    public override void OnStart()
    {
        base.OnStart();
        BindRayTarget();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // The RayTarget can arrive after this component (the seat prop is still being assembled, or a
        // peer's copy syncs in later), so keep trying until the binding sticks.
        if (_rayTarget == null || _rayTarget.IsDestroyed)
            BindRayTarget();
    }

    private void BindRayTarget()
    {
        if (Slot == null || Slot.IsDestroyed)
            return;

        var target = Slot.GetComponent<RayTarget>();
        if (target == null)
        {
            // Only the owner may mint it; a denied write during the join ownership lag just means the
            // next update retries.
            target = Slot.AttachComponent<RayTarget>();
            target.HoverRadius.Value = 0.25f;
        }

        if (ReferenceEquals(target, _rayTarget))
            return;

        if (_rayTarget != null && !_rayTarget.IsDestroyed)
        {
            _rayTarget.Activated -= OnActivated;
            _rayTarget.HoverEntered -= OnHoverEntered;
            _rayTarget.HoverExited -= OnHoverExited;
        }

        _rayTarget = target;
        _rayTarget.Activated += OnActivated;
        _rayTarget.HoverEntered += OnHoverEntered;
        _rayTarget.HoverExited += OnHoverExited;
    }

    // Aimed at, so it lights up - but only if this user could actually use it. A taken chair that
    // glows invitingly is worse than one that does not, because the press it invites does nothing.
    private void OnHoverEntered()
    {
        var anchor = Anchor;
        if (anchor == null)
            return;
        var localUser = World?.LocalUser;
        anchor.Highlight = localUser != null
            && (anchor.AnchoredUser == localUser ? AllowRelease.Value : AllowSit.Value && anchor.CanAnchor(localUser));
    }

    private void OnHoverExited()
    {
        var anchor = Anchor;
        if (anchor != null)
            anchor.Highlight = false;
    }

    // Written against the anchor, not the chair: the same trigger drives a vehicle or a bed without
    // knowing it is one. -xlinka
    private void OnActivated(float3 point)
    {
        var anchor = Anchor;
        if (anchor == null)
            return;

        var localUser = World?.LocalUser;
        if (localUser == null)
            return;

        if (anchor.AnchoredUser == localUser)
        {
            if (AllowRelease.Value)
                anchor.Release();
            return;
        }

        // Somebody else is in it: the press does nothing rather than turfing them out.
        if (anchor.IsOccupied)
            return;

        if (AllowSit.Value)
            anchor.Anchor(localUser);
    }

    bool ISeatTrigger.TrySit(User user)
    {
        var seat = Seat;
        return seat != null && seat.TrySit(user);
    }

    bool ISeatTrigger.Release()
    {
        var seat = Seat;
        return seat != null && seat.Release();
    }

    public override void OnDestroy()
    {
        // The highlight is local state on the anchor; leaving it lit because the trigger went away
        // would strand a glowing chair nobody is pointing at.
        var anchor = Anchor;
        if (anchor != null)
            anchor.Highlight = false;

        if (_rayTarget != null && !_rayTarget.IsDestroyed)
        {
            _rayTarget.HoverEntered -= OnHoverEntered;
            _rayTarget.HoverExited -= OnHoverExited;
            _rayTarget.Activated -= OnActivated;
        }
        _rayTarget = null!;
        base.OnDestroy();
    }
}
