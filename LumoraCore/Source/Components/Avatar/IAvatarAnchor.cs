// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Anything that can hold a user's body in place.
//
// We had exactly one of these and it was called Seat, which is a chair. A chair is one thing you can
// be held by; the others are a vehicle, a bed, a pose stand, a rail you lean on, a machine you climb
// into, a grabbable somebody is carrying you in. None of those are chairs and all of them want the
// same five facts: is it taken, who by, put this user in it, let them out, and light it up when
// somebody is pointing at it.
//
// So the five facts are the contract and the chair is one implementation. A tool that highlights what
// it is aimed at, a menu that offers "get out", a world that wants to know which of its anchors are
// free - none of them should have to know what kind of thing they are talking to, and with a concrete
// Seat as the only type, every one of them did.
//
// Occupancy is ONE user. Something that holds several is several anchors, which is also how it looks
// to the person using it: a bench with three places is three places. -xlinka
public interface IAvatarAnchor
{
    // The component itself, so a caller can reach its slot, its enabled flag and its world.
    Component Component { get; }

    bool IsOccupied { get; }

    User? AnchoredUser { get; }

    // Lit because something is pointing at it. Local and cosmetic: a highlight is about one person's
    // aim and has no business travelling to anybody else.
    bool Highlight { get; set; }

    // Would this user be allowed in right now? Asked before offering the interaction, so a taken or
    // filtered anchor can be shown as unavailable rather than refusing after the press.
    bool CanAnchor(User user);

    // True when the user is now held. Only ever called on that user's own peer: moving somebody
    // else's body is not a thing another peer may do.
    bool Anchor(User user);

    // True when somebody was released.
    bool Release();
}

// A separate say in who may use an anchor.
//
// The chair grew its own rules - an owner-only flag and an allowed-user list - and they are good
// rules, and they are also the only two that will ever exist as long as they live inside the chair.
// "Only while the door is open", "only people in this group", "only if you are holding a ticket" are
// all somebody else's rule about somebody else's world, and none of them belong in a seat.
//
// Every filter on the anchor's slot gets a veto. They can only refuse: an anchor that says no stays
// no, or a filter could hand out access the anchor itself had already declined. -xlinka
public interface IAvatarAnchorUserFilter
{
    bool CanAnchor(User user);
}

// Asking the filters on a slot. Kept here rather than in the chair so every anchor gets it the same
// way and nobody has to remember to write the loop.
public static class AnchorFilters
{
    public static bool Allow(Slot? slot, User? user)
    {
        if (user == null || user.IsDestroyed)
            return false;
        if (slot == null || slot.IsDestroyed)
            return true;

        foreach (var component in slot.GetComponents<Component>())
        {
            if (component is not IAvatarAnchorUserFilter filter || component.IsDestroyed || !component.Enabled.Value)
                continue;
            try
            {
                if (!filter.CanAnchor(user))
                    return false;
            }
            catch (System.Exception ex)
            {
                // A filter that throws is a filter that has not decided. Refuse, because the safe
                // answer to "may this person sit here" is no.
                Logging.Logger.Error($"AnchorFilters: a filter threw, refusing: {ex.Message}");
                return false;
            }
        }
        return true;
    }
}
