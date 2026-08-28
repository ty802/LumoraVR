// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Gated on real world state, not an unconditional allow: a locked world (Social/Event) is a bounded
// experience the host froze, so free-fly that lets a user leave the authored space (noclip/fly) is
// denied there; Builder worlds allow everything. No per-module permission field on the data model yet -
// when one exists this is where it gets consulted, host-authoritative like the rest of the permission surface.
public class LocomotionPermissions
{
    private readonly World? _world;

    public LocomotionPermissions(World? world)
    {
        _world = world;
    }

    public bool CanUseLocomotion(LocomotionModule module)
    {
        if (module == null)
            return false;

        // The "no locomotion" fallback is always available so the controller never strands a user.
        if (module is NullLocomotionModule)
            return true;

        // Free-fly (noclip) escapes the bounded world. A host-locked world (Social/Event) is meant to keep
        // users within the authored space, so deny it there. Ground locomotion stays allowed in every mode.
        if (IsFreeFly(module) && IsWorldLocked())
            return false;

        return true;
    }

    // always true; ground locomotion is never gated
    public bool CanUseAnyLocomotion() => true;

    // Free-flight modules move the rig directly with no ground/collision constraint, letting a user leave the
    // playable space. NoclipLocomotion is the one such module today; gate by base type so future fly modes
    // inherit the same restriction without editing this list.
    private static bool IsFreeFly(LocomotionModule module) => module is NoclipLocomotion;

    // Social / Event worlds are host-frozen, bounded experiences. Read the floor from the permission
    // policy rather than re-deriving it here, or this drifts from what the gate actually enforces.
    // Mode is baked host-authoritatively at host time and can't be toggled live.
    private bool IsWorldLocked()
    {
        var world = _world;
        if (world == null)
            return false;
        return Lumora.Warden.WorldModePolicy.SocialLockFloor(world.Mode);
    }
}
