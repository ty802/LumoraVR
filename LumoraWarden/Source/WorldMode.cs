// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Warden;

// How a world session behaves with respect to editing. Chosen at host time from the world's allowed
// modes and baked into the session - it is NOT a live toggle, and the lock is enforced
// host-authoritatively at the datamodel gate (so no client, and not the host, can bypass it).
public enum WorldMode
{
    // Full in-world editing: spawn, build, manipulate, dev tools and inspectors. The default
    // creation world.
    Builder,

    // Social space: the authored world is frozen for EVERYONE (including the host) - no building,
    // no dev tools, no inspectors. Users keep their own avatar and may bring their own items.
    Social,

    // Like Social but stricter: users can't spawn items either - view and interact only.
    Event
}

// The per-mode policy data. This is the single definition of the Social/Event lock floor: read it
// here rather than re-deriving "mode != Builder" at each call site, or the floor drifts between the
// gate and whatever consults it (locomotion, tool availability, UI). -xlinka
public static class WorldModePolicy
{
    // Whether the mode freezes the authored world for EVERYONE, host included. The engine applies
    // this as PermissionEngine.SocialLock; nothing else may loosen it.
    public static bool SocialLockFloor(WorldMode mode) => mode switch
    {
        WorldMode.Social => true,
        WorldMode.Event => true,
        _ => false
    };

    // Whether users may still bring and handle their own items in this mode.
    public static bool AllowsOwnItems(WorldMode mode) => mode != WorldMode.Event;
}
