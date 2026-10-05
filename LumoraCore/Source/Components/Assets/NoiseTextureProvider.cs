// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Per-pixel random noise: grain, static, a dither or a fur shell's strand map. Monochrome picks each
// pixel between Low and High; otherwise every channel is random on its own. Gain and Bias then scale and
// shift every channel (value * Gain + Bias), held to 0..1. The same Seed gives the same texture on every
// machine. Built once per change, never per frame. -xlinka
[ComponentCategory("Assets/Textures")]
public sealed class NoiseTextureProvider : DynamicAssetProvider<TextureAsset>
{
    private const int MaxSize = 2048;

    public readonly Sync<int> Width;
    public readonly Sync<int> Height;
    public readonly Sync<int> Seed;
    public readonly Sync<bool> Monochrome;
    public readonly Sync<color> Low;
    public readonly Sync<color> High;
    public readonly Sync<float> Gain;
    public readonly Sync<float> Bias;
    public readonly Sync<bool> GenerateMipmaps;

    public NoiseTextureProvider()
    {
        Width = new Sync<int>(this, 128);
        Height = new Sync<int>(this, 128);
        Seed = new Sync<int>(this, 0);
        Monochrome = new Sync<bool>(this, true);
        Low = new Sync<color>(this, color.Black);
        High = new Sync<color>(this, color.White);
        Gain = new Sync<float>(this, 1f);
        Bias = new Sync<float>(this, 0f);
        GenerateMipmaps = new Sync<bool>(this, false);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void OnAssetCleared() { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int width = System.Math.Clamp(Width.Value, 1, MaxSize);
        int height = System.Math.Clamp(Height.Value, 1, MaxSize);
        var random = new Random(Seed.Value);
        bool mono = Monochrome.Value;
        var low = Low.Value;
        var high = High.Value;
        float gain = Gain.Value;
        float bias = Bias.Value;
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            float r, g, b, a;
            if (mono)
            {
                float t = (float)random.NextDouble();
                r = low.r + (high.r - low.r) * t;
                g = low.g + (high.g - low.g) * t;
                b = low.b + (high.b - low.b) * t;
                a = low.a + (high.a - low.a) * t;
            }
            else
            {
                r = (float)random.NextDouble();
                g = (float)random.NextDouble();
                b = (float)random.NextDouble();
                a = (float)random.NextDouble();
            }
            pixels[i] = ToByte(r * gain + bias);
            pixels[i + 1] = ToByte(g * gain + bias);
            pixels[i + 2] = ToByte(b * gain + bias);
            pixels[i + 3] = ToByte(a * gain + bias);
        }
        asset.SetImageData(pixels, width, height, GenerateMipmaps.Value);
    }

    private static byte ToByte(float value)
    {
        if (!(value > 0f)) return 0;
        if (value >= 1f) return 255;
        return (byte)(value * 255f + 0.5f);
    }
}
