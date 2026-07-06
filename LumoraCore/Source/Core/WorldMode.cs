// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// How a world session behaves with respect to editing. Chosen at host time from the world's allowed
/// modes and baked into the session - it is NOT a live toggle, and the lock is enforced
/// host-authoritatively at the datamodel gate (so no client, and not the host, can bypass it).
/// </summary>
public enum WorldMode
{
    /// <summary>
    /// Full in-world editing: spawn, build, manipulate, dev tools and inspectors. The default
    /// creation world.
    /// </summary>
    Builder,

    /// <summary>
    /// Social space: the authored world is frozen for EVERYONE (including the host) - no building,
    /// no dev tools, no inspectors. Users keep their own avatar and may bring their own items.
    /// </summary>
    Social,

    /// <summary>
    /// Like <see cref="Social"/> but stricter: users can't spawn items either - view and interact only.
    /// </summary>
    Event
}
