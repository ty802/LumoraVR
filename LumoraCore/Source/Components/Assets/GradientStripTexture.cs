// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Procedural gradient texture: From blends to To along the chosen orientation, with an exponent
/// shaping the falloff. Radial runs from the center outward (soft glow discs).
/// </summary>
[ComponentCategory("Assets/Textures/Gradient")]
public sealed class GradientStripTexture : DynamicAssetProvider<TextureAsset>
{
    public enum StripOrientation
    {
        Vertical,
        Horizontal,
        Radial
    }

    public readonly Sync<color> From;
    public readonly Sync<color> To;
    public readonly Sync<float> Exp;
    public readonly Sync<int> Size;
    public readonly Sync<StripOrientation> Orientation;

    public GradientStripTexture()
    {
        From = new Sync<color>(this, new color(1f, 1f, 1f, 1f));
        To = new Sync<color>(this, new color(0f, 0f, 0f, 0f));
        Exp = new Sync<float>(this, 1f);
        Size = new Sync<int>(this, 32);
        Orientation = new Sync<StripOrientation>(this, StripOrientation.Vertical);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int size = System.Math.Clamp(Size.Value, 2, 512);
        // Strips are 1 texel wide across the gradient; radial needs the full square.
        int width = Orientation.Value == StripOrientation.Horizontal || Orientation.Value == StripOrientation.Radial ? size : 1;
        int height = Orientation.Value == StripOrientation.Vertical || Orientation.Value == StripOrientation.Radial ? size : 1;

        var pixels = new byte[width * height * 4];
        var from = From.Value;
        var to = To.Value;
        float exp = System.Math.Clamp(Exp.Value, 0.05f, 16f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float t = Orientation.Value switch
                {
                    StripOrientation.Vertical => height > 1 ? (float)y / (height - 1) : 0f,
                    StripOrientation.Horizontal => width > 1 ? (float)x / (width - 1) : 0f,
                    _ => RadialT(x, y, size),
                };
                t = MathF.Pow(System.Math.Clamp(t, 0f, 1f), exp);
                WriteColor(pixels, (y * width + x) * 4, Lerp(in from, in to, t));
            }
        }

        asset.SetImageData(pixels, width, height, false);
    }

    protected override void OnAssetCleared() { }

    private static float RadialT(int x, int y, int size)
    {
        float half = (size - 1) * 0.5f;
        float dx = (x - half) / half;
        float dy = (y - half) / half;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static color Lerp(in color a, in color b, float t)
        => new color(
            a.r + (b.r - a.r) * t,
            a.g + (b.g - a.g) * t,
            a.b + (b.b - a.b) * t,
            a.a + (b.a - a.a) * t);

    private static void WriteColor(byte[] pixels, int index, in color value)
    {
        pixels[index] = ToByte(value.r);
        pixels[index + 1] = ToByte(value.g);
        pixels[index + 2] = ToByte(value.b);
        pixels[index + 3] = ToByte(value.a);
    }

    private static byte ToByte(float value)
    {
        if (value <= 0f) return 0;
        if (value >= 1f) return 255;
        return (byte)(value * 255f + 0.5f);
    }
}
