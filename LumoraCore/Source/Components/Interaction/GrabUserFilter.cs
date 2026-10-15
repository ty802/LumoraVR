// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Who may pick something up, by whether it sits under a person (worn, carried in a pouch, part of an
// avatar). A usability gate like TouchUserFilter, not a security one: the permission gate still decides
// whether the grab's writes land at all.
public enum GrabUserFilter
{
    Anyone,

    // Under a person, only that person; standing free in the world, anyone.
    WearerWhenWorn,

    // Only while it is under a person, and only by that person.
    WearerOnly,

    // Anyone but the person it is under; anyone at all when it stands free.
    NotWearer,
}

public static class GrabUserFilterExtensions
{
    public static bool Allows(this GrabUserFilter filter, Slot? slot, User? user)
    {
        if (filter == GrabUserFilter.Anyone)
            return true;
        if (slot == null || user == null)
            return false;
        var wearer = slot.ActiveUserRoot?.ActiveUser;
        return filter switch
        {
            GrabUserFilter.WearerWhenWorn => wearer == null || ReferenceEquals(wearer, user),
            GrabUserFilter.WearerOnly => wearer != null && ReferenceEquals(wearer, user),
            GrabUserFilter.NotWearer => wearer == null || !ReferenceEquals(wearer, user),
            _ => false,
        };
    }
}
