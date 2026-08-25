// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Touch;

// Who is allowed to work a touch control. This is a USABILITY gate, not a security gate - the
// security gate is the permission system that decides whether the control's synced writes land at
// all. Use it to stop other people mashing the buttons on the thing you are wearing. -xlinka
public enum TouchUserFilter
{
    Anyone,

    // When the control rides under a user (worn, held, part of an avatar) only that user may work
    // it. A control standing free in the world stays open to everyone.
    ActiveUserOnly,

    // Only the user whose RefID namespace the control was minted in - whoever spawned or authored
    // it. Host-authored content resolves to the host.
    OwnerOnly,
}

public static class TouchUserFilterExtensions
{
    // An unresolvable owner fails closed on OwnerOnly.
    public static bool Allows(this TouchUserFilter filter, IWorldElement element, User? user)
    {
        if (user == null || element == null)
            return false;

        switch (filter)
        {
            case TouchUserFilter.Anyone:
                return true;

            case TouchUserFilter.ActiveUserOnly:
            {
                var wearer = ResolveSlot(element)?.ActiveUserRoot?.ActiveUser;
                return wearer == null || wearer == user;
            }

            case TouchUserFilter.OwnerOnly:
            {
                var owner = ResolveOwner(element);
                return owner != null && owner == user;
            }

            default:
                return false;
        }
    }

    private static Slot? ResolveSlot(IWorldElement element)
        => element switch
        {
            Slot slot => slot,
            Component component => component.Slot,
            _ => null,
        };

    // The RefID byte an element was minted in IS its ownership record: the host authors in the
    // authority byte, every client in its own allocated byte. Match that byte back to a user rather
    // than storing a separate owner ref that a client could just rewrite. Host-authored content
    // lands on whichever user object is itself minted in the authority byte, so no special case. -xlinka
    private static User? ResolveOwner(IWorldElement element)
    {
        var world = element.World;
        if (world == null)
            return null;

        byte ownerByte = element.ReferenceID.GetUserByte();
        foreach (var user in world.GetAllUsers())
        {
            if (user == null || user.IsDestroyed)
                continue;
            if (user.AllocationID.Value == ownerByte || user.ReferenceID.GetUserByte() == ownerByte)
                return user;
        }
        return null;
    }
}
