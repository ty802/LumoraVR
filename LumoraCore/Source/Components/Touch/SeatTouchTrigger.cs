// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar;

namespace Lumora.Core.Components.Touch;

// Slap the seat to sit in it. Slap it again to get up.
//
// A touch control that happens to drive a seat, so it inherits the whole probe path: a fingertip works
// it in VR, the beam works it on desktop, and the same accept flags and user filter apply. The seating
// itself is not reimplemented here - the press forwards straight to the seat, which stays the one and
// only place the transform bookkeeping lives. -xlinka
[ComponentCategory("Interaction/Touch")]
public class SeatTouchTrigger : TouchControl, ISeatTrigger
{
    // Falls back to a seat on this slot.
    public readonly SyncRef<Seat> TargetSeat;

    // Allow a touch to seat the toucher.
    public readonly Sync<bool> AllowSit;

    // Allow a touch to stand the toucher back up.
    public readonly Sync<bool> AllowRelease;

    public readonly Sync<bool> IsHovering;

    public SeatTouchTrigger()
    {
        TargetSeat = new SyncRef<Seat>(this);
        AllowSit = new Sync<bool>(this, true);
        AllowRelease = new Sync<bool>(this, true);
        IsHovering = new Sync<bool>(this, false);

        // Sitting is a heavier commitment than pressing a button, so it gets the heavier pulse.
        ContactHaptics.Value = TouchHaptics.Strong;
    }

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

    protected override string? PointerLabel
    {
        get
        {
            var seat = Seat;
            if (seat == null)
                return base.PointerLabel;
            return seat.IsLocalUserSeated ? "Stand up" : "Sit";
        }
    }

    protected override void OnTouchContact(in TouchContact contact)
    {
        if (contact.Hover == TouchPhase.Begin)
        {
            IsHovering.Value = true;
            PulseHover(in contact);
        }
        else if (contact.Hover == TouchPhase.End)
        {
            IsHovering.Value = false;
        }

        // Only the leading edge. A hand resting against the seat back would otherwise sit and stand
        // once per frame for as long as it stays there. -xlinka
        if (contact.Contact != TouchPhase.Begin)
            return;

        var user = contact.User;
        var seat = Seat;
        if (user == null || seat == null)
            return;

        if (seat.IsLocalUserSeated)
        {
            if (AllowRelease.Value && seat.Release())
                PulseContact(in contact);
            return;
        }

        // Somebody else is in it. Touching does nothing rather than turfing them out.
        if (seat.IsOccupied)
            return;

        if (AllowSit.Value && seat.TrySit(user))
            PulseContact(in contact);
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
}
