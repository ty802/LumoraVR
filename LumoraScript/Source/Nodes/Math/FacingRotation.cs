// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// The rotation that turns forward (+z) to face along forward, with up as near to up as it can.
// An up of zero means world up. The library's LookRotation hands back the inverse, hence the flip.
[Node("Math/Rotations/Look Rotation")]
public sealed class FacingRotation : ValueNode
{
    [return: Port("rotation")]
    public static floatQ Run(float3 forward, float3 up)
    {
        if (forward.LengthSquared < 1e-12f)
            return floatQ.Identity;
        if (up.LengthSquared < 1e-12f)
            up = float3.Up;
        return floatQ.LookRotation(forward, up).Inverse;
    }
}
