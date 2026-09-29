// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// HSV and HSL for the color nodes, on top of the math library's HSV. A NaN never reaches a channel: NaN
// going in reads as 0 and an infinity as the largest float, then the hue wraps and the rest clamp. HSL is
// HSV seen another way: lightness l at saturation s is brightness l + s min(l, 1 - l). -xlinka
internal static class ColorModels
{
    public static float Finite(float value)
        => float.IsNaN(value) ? 0f : Math.Clamp(value, float.MinValue, float.MaxValue);

    public static float Unit(float value) => value > 0f ? (value < 1f ? value : 1f) : 0f;

    public static void ToHsv(float r, float g, float b, out float hue, out float saturation, out float brightness)
    {
        (hue, saturation, brightness) = new colorHDR(r, g, b).ToHSV();
        if (float.IsNaN(hue))
            hue = 0f;
        saturation = Unit(saturation);
    }

    public static color FromHsv(float hue, float saturation, float brightness, float alpha)
        => color.FromHSV(Finite(hue), Finite(saturation), Finite(brightness), alpha);

    public static colorHDR FromHsvHdr(float hue, float saturation, float brightness, float alpha)
        => colorHDR.FromHSV(Finite(hue), Finite(saturation), Finite(brightness), alpha);

    public static void ToHsl(color value, out float hue, out float saturation, out float lightness)
    {
        ToHsv(value.r, value.g, value.b, out hue, out float s, out float v);
        lightness = v * (1f - s / 2f);
        float room = MathF.Min(lightness, 1f - lightness);
        saturation = room > 0f ? Unit((v - lightness) / room) : 0f;
    }

    public static color FromHsl(float hue, float saturation, float lightness, float alpha)
    {
        float s = Unit(saturation), l = Unit(lightness);
        float v = l + s * MathF.Min(l, 1f - l);
        return FromHsv(hue, v > 0f ? 2f * (1f - l / v) : 0f, v, alpha);
    }
}
