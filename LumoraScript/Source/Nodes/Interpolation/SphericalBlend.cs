// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// A spherical blend of two vectors held in the first lanes of a float4: the direction turns along the
// great circle at an even rate while the length goes straight from one length to the other, so unit
// vectors stay unit and any others blend lengths too. The length never passes below zero, which
// would flip the direction. A zero vector has no direction and takes the other's. Exactly opposite
// directions have no one great circle; they turn through a fixed one, the same every time. -xlinka
internal static class SphericalBlend
{
    public static float4 Vectors(float4 a, float4 b, float t, int lanes)
    {
        float la = a.Length, lb = b.Length;
        float length = MathF.Max(0f, la + (lb - la) * t);
        if (!(la > 0f))
            return lb > 0f ? b * (length / lb) : float4.Zero;
        if (!(lb > 0f))
            return a * (length / la);

        float4 ua = a / la, ub = b / lb;
        float dot = Math.Clamp(float4.Dot(ua, ub), -1f, 1f);
        float4 across = ub - ua * dot;
        float acrossLength = across.Length;
        if (acrossLength < 1e-6f)
        {
            if (dot > 0f)
                return ua * length;
            across = Perpendicular(ua, lanes);
        }
        else
        {
            across /= acrossLength;
        }

        float turn = MathF.Acos(dot) * t;
        return (ua * MathF.Cos(turn) + across * MathF.Sin(turn)) * length;
    }

    public static float2 Vectors(float2 a, float2 b, float t)
    {
        var v = Vectors(new float4(a, 0f, 0f), new float4(b, 0f, 0f), t, 2);
        return new float2(v.x, v.y);
    }

    public static float3 Vectors(float3 a, float3 b, float t) => Vectors(new float4(a, 0f), new float4(b, 0f), t, 3).xyz;

    public static float4 Vectors(float4 a, float4 b, float t) => Vectors(a, b, t, 4);

    public static color Vectors(color a, color b, float t)
    {
        var v = Vectors(new float4(a.r, a.g, a.b, a.a), new float4(b.r, b.g, b.b, b.a), t, 4);
        return new color(v.x, v.y, v.z, v.w);
    }

    public static colorHDR Vectors(colorHDR a, colorHDR b, float t)
    {
        var v = Vectors(new float4(a.r, a.g, a.b, a.a), new float4(b.r, b.g, b.b, b.a), t, 4);
        return new colorHDR(v.x, v.y, v.z, v.w);
    }

    // Along the shorter turn. A zero rotation counts as no turn.
    public static floatQ Rotations(floatQ a, floatQ b, float t) => Turns.Unit(floatQ.Slerp(Turns.Unit(a), Turns.Unit(b), t));

    // The unit lane a is least along, with a's share taken out.
    private static float4 Perpendicular(float4 a, int lanes)
    {
        int pick = 0;
        for (int i = 1; i < lanes; i++)
        {
            if (MathF.Abs(a[i]) < MathF.Abs(a[pick]))
                pick = i;
        }
        var axis = float4.Zero;
        axis[pick] = 1f;
        var across = axis - a * a[pick];
        return across / across.Length;
    }
}
