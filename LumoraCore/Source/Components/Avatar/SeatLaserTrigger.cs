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
            _rayTarget.Activated -= OnActivated;

        _rayTarget = target;
        _rayTarget.Activated += OnActivated;
    }

    private void OnActivated(float3 point)
    {
        var seat = Seat;
        if (seat == null)
            return;

        var localUser = World?.LocalUser;
        if (localUser == null)
            return;

        if (seat.IsLocalUserSeated)
        {
            if (AllowRelease.Value)
                seat.Release();
            return;
        }

        // Somebody else's seat: the press does nothing rather than kicking them out of it.
        if (seat.IsOccupied)
            return;

        if (AllowSit.Value)
            seat.TrySit(localUser);
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
        if (_rayTarget != null && !_rayTarget.IsDestroyed)
            _rayTarget.Activated -= OnActivated;
        _rayTarget = null!;
        base.OnDestroy();
    }
}
