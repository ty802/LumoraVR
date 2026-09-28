// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// 0 below edge0, 1 past edge1, and an eased curve between.
[Node("Math/Smooth Step")]
public sealed class SmoothStep : ValueNode
{
    public static float Run(float edge0, float edge1, float value)
    {
        float t = edge0 == edge1 ? (value < edge0 ? 0f : 1f) : MathF.Max(0f, MathF.Min(1f, (value - edge0) / (edge1 - edge0)));
        return t * t * (3f - 2f * t);
    }

    public static double Run(double edge0, double edge1, double value)
    {
        double t = edge0 == edge1 ? (value < edge0 ? 0d : 1d) : Math.Max(0d, Math.Min(1d, (value - edge0) / (edge1 - edge0)));
        return t * t * (3d - 2d * t);
    }
}
