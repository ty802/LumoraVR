// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// The signed turn from one 2D direction to another, in degrees and in radians, -180 to 180: positive turning
// from +x toward +y, the way Atan2 counts. Lengths do not matter, and a zero vector turns nothing.
[Node("Math/Vectors/Delta Angle")]
public sealed class DeltaAngle : ValueNode
{
    [return: Port("degrees")]
    public static float Run(float2 from, float2 to, out float radians)
    {
        double turn = Math.Atan2((double)from.x * to.y - (double)from.y * to.x, (double)from.x * to.x + (double)from.y * to.y);
        radians = (float)turn;
        return (float)(turn * (180d / Math.PI));
    }
}
