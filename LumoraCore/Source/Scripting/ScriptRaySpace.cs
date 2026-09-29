// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// A ray given in a slot's own space, brought into the world: the start as a point, the direction as a
// direction, and the range stretched the way the slot stretches along that direction, so a ray a metre
// long in a slot scaled by two reaches two metres. No root leaves the ray as it is; a root the script
// cannot see casts nothing.
internal static class ScriptRaySpace
{
    public static bool ToWorld(ScriptContext ctx, Slot? root, ref float3 origin, ref float3 direction, ref float range)
    {
        if (root == null)
            return true;
        if (!ScriptReads.Visible(ctx, root))
            return false;
        origin = root.LocalPointToGlobal(origin);
        if (ScriptPhysics.IsFinite(direction) && direction.LengthSquared >= 1e-12f)
        {
            var along = root.LocalDirectionToGlobal(direction.Normalized);
            range *= along.Length;
            direction = along;
        }
        return true;
    }
}
