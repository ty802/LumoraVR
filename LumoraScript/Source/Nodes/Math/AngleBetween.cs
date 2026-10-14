// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// The angle between two directions in degrees, 0 to 180, whichever way round. A zero vector has no direction
// and reads as at right angles to anything. -xlinka
[Node("Math/Vectors/Angle Between")]
public sealed class AngleBetween : ValueNode
{
    [return: Port("degrees")]
    public static float Run(float3 a, float3 b)
    {
        var x = Unit(a);
        var y = Unit(b);
        float dot = Math.Clamp(x.x * y.x + x.y * y.y + x.z * y.z, -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }

    private static float3 Unit(float3 v)
    {
        float length = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        return length > 1e-12f ? new float3(v.x / length, v.y / length, v.z / length) : new float3(0f, 0f, 0f);
    }
}
