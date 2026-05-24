// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Textures/Checker")]
public sealed class CheckerTextureProvider : DynamicAssetProvider<TextureAsset>
{
    public readonly Sync<int> Width;
    public readonly Sync<int> Height;
    public readonly Sync<int> CellSize;
    public readonly Sync<color> ColorA;
    public readonly Sync<color> ColorB;
    public readonly Sync<bool> GenerateMipmaps;

    public CheckerTextureProvider()
    {
        Width = new Sync<int>(this, 64);
        Height = new Sync<int>(this, 64);
        CellSize = new Sync<int>(this, 8);
        ColorA = new Sync<color>(this, new color(0.10f, 0.62f, 0.85f, 1f));
        ColorB = new Sync<color>(this, new color(0.95f, 0.28f, 0.55f, 1f));
        GenerateMipmaps = new Sync<bool>(this, false);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int width = Clamp(Width.Value, 1, 512);
        int height = Clamp(Height.Value, 1, 512);
        int cellSize = Clamp(CellSize.Value, 1, 512);
        var pixels = new byte[width * height * 4];

        var a = ColorA.Value;
        var b = ColorB.Value;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool cell = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                WriteColor(pixels, (y * width + x) * 4, cell ? a : b);
            }
        }

        asset.SetImageData(pixels, width, height, GenerateMipmaps.Value);
    }

    protected override void OnAssetCleared() { }

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

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
