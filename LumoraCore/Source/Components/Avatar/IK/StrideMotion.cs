// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

internal static class StrideMotion
{
    public static float Ease(float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return t * t * t * (10f + t * (-15f + 6f * t));
    }

    public static float Lift(float t, float power = 3f)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        float arch = 4f * t * (1f - t);
        return MathF.Pow(arch, MathF.Max(power, 2.01f));
    }

    public static float Height(float3 from, float3 to, float maximum)
    {
        float3 travel = to - from;
        travel.y = 0f;
        return MathF.Min(MathF.Max(maximum, 0f), travel.Length * 0.16f);
    }
}
