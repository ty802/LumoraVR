// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;

namespace Lumora.Core.Components;

// Grid and label spacing for the curve graph: a round step (1, 2 or 5 times a power of ten) that puts
// about the asked-for number of lines across the span, and numbers printed to that step's precision.
public static class CurveTicks
{
    public static float Step(float span, int target)
    {
        if (!(span > 0f) || !float.IsFinite(span))
            return 1f;
        float raw = span / System.Math.Max(1, target);
        float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
        float normalized = raw / magnitude;
        float nice = normalized < 1.5f ? 1f : normalized < 3.5f ? 2f : normalized < 7.5f ? 5f : 10f;
        return nice * magnitude;
    }

    public static float First(float min, float step) => MathF.Ceiling(min / step) * step;

    public static string Format(float value, float step)
    {
        int decimals = System.Math.Clamp((int)MathF.Ceiling(-MathF.Log10(step) - 1e-4f), 0, 4);
        if (MathF.Abs(value) < step * 1e-3f)
            value = 0f;
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    // A key's time or value in the key strip: enough places to see a nudge, none of the float noise.
    public static string Exact(float value)
    {
        if (MathF.Abs(value) < 1e-6f)
            value = 0f;
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
