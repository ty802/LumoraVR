// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

/// <summary>
/// The encoding a color's RGB components are stored in. Authored swatches, hex strings, and most UI colors
/// are <see cref="sRGB"/> (perceptually-spaced gamma); lighting, blending, and interpolation are only
/// correct in <see cref="Linear"/>. Colors are not tagged with a profile - convert explicitly at the
/// boundary with the <c>ToLinear()</c> / <c>ToGamma()</c> helpers on <c>color</c> / <c>colorHDR</c>. -xlinka
/// </summary>
public enum ColorProfile
{
    Linear,
    sRGB,
}

/// <summary>
/// sRGB transfer functions (IEC 61966-2-1, the exact piecewise curve). Per-component; alpha is never
/// gamma-encoded. The linear segment also covers values <= 0, and the power segment extrapolates past
/// 1.0 so HDR values convert sanely. -xlinka
/// </summary>
public static class ColorSpace
{
    /// <summary>sRGB (gamma) component -> linear component.</summary>
    public static float GammaToLinear(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>Linear component -> sRGB (gamma) component.</summary>
    public static float LinearToGamma(float c)
        => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
}
