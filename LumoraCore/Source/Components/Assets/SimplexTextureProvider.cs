// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Smooth simplex noise across a texture, blended from Background to Foreground: clouds, a ripple map for a
// displacement, dirt for a mask. Scale is how many noise features span the texture on each axis and Offset
// slides the pattern. Sample3D reads a slice of 3D noise at Depth instead of the plane, so stepping Depth
// morphs the pattern rather than moving it. Built once per change, never per frame. -xlinka
[ComponentCategory("Assets/Textures")]
public sealed class SimplexTextureProvider : DynamicAssetProvider<TextureAsset>
{
    private const int MaxSize = 2048;

    public readonly Sync<int> Width;
    public readonly Sync<int> Height;
    public readonly Sync<float2> Scale;
    public readonly Sync<float2> Offset;
    public readonly Sync<color> Background;
    public readonly Sync<color> Foreground;
    public readonly Sync<bool> Sample3D;
    public readonly Sync<float> Depth;
    public readonly Sync<bool> GenerateMipmaps;

    public SimplexTextureProvider()
    {
        Width = new Sync<int>(this, 128);
        Height = new Sync<int>(this, 128);
        Scale = new Sync<float2>(this, float2.One);
        Offset = new Sync<float2>(this, float2.Zero);
        Background = new Sync<color>(this, color.Black);
        Foreground = new Sync<color>(this, color.White);
        Sample3D = new Sync<bool>(this, false);
        Depth = new Sync<float>(this, 0f);
        GenerateMipmaps = new Sync<bool>(this, false);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void OnAssetCleared() { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int width = System.Math.Clamp(Width.Value, 1, MaxSize);
        int height = System.Math.Clamp(Height.Value, 1, MaxSize);
        var scale = Scale.Value;
        var offset = Offset.Value;
        var background = Background.Value;
        var foreground = Foreground.Value;
        bool volume = Sample3D.Value;
        float depth = Depth.Value;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height * scale.y + offset.y;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width * scale.x + offset.x;
                float n = volume ? Simplex.Noise3D(u, v, depth) : Simplex.Noise2D(u, v);
                float t = n * 0.5f + 0.5f;
                int i = (y * width + x) * 4;
                pixels[i] = ToByte(background.r + (foreground.r - background.r) * t);
                pixels[i + 1] = ToByte(background.g + (foreground.g - background.g) * t);
                pixels[i + 2] = ToByte(background.b + (foreground.b - background.b) * t);
                pixels[i + 3] = ToByte(background.a + (foreground.a - background.a) * t);
            }
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
