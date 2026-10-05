// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// A whole grid in one texture: lines of LineColor over Background, Spacing pixels apart, LineWidth pixels
// thick and centred on each line, the first one Offset pixels in. Spacing, Offset and LineWidth are per axis
// (x for the vertical lines, y for the horizontal ones); a width of 0 leaves that axis without lines. Unlike
// GridCellTextureProvider, which is one cell meant to be tiled, this draws every line itself, so it can be
// stretched over a floor or a ruler as it is. Built once per change, never per frame. -xlinka
[ComponentCategory("Assets/Textures")]
public sealed class GridTextureProvider : DynamicAssetProvider<TextureAsset>
{
    private const int MaxSize = 2048;

    public readonly Sync<int> Width;
    public readonly Sync<int> Height;
    public readonly Sync<color> Background;
    public readonly Sync<color> LineColor;
    public readonly Sync<float2> Spacing;
    public readonly Sync<float2> Offset;
    public readonly Sync<float2> LineWidth;
    public readonly Sync<bool> GenerateMipmaps;

    public GridTextureProvider()
    {
        Width = new Sync<int>(this, 128);
        Height = new Sync<int>(this, 128);
        Background = new Sync<color>(this, color.White);
        LineColor = new Sync<color>(this, color.Black);
        Spacing = new Sync<float2>(this, new float2(32f, 32f));
        Offset = new Sync<float2>(this, new float2(16f, 16f));
        LineWidth = new Sync<float2>(this, new float2(4f, 4f));
        GenerateMipmaps = new Sync<bool>(this, true);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void OnAssetCleared() { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int width = System.Math.Clamp(Width.Value, 1, MaxSize);
        int height = System.Math.Clamp(Height.Value, 1, MaxSize);
        var pixels = new byte[width * height * 4];
        var background = Background.Value;
        var line = LineColor.Value;
        for (int i = 0; i < pixels.Length; i += 4)
            Write(pixels, i, background);

        int spacingX = System.Math.Max(1, (int)System.Math.Abs(Spacing.Value.x));
        int spacingY = System.Math.Max(1, (int)System.Math.Abs(Spacing.Value.y));
        int lineX = (int)System.Math.Abs(LineWidth.Value.x);
        int lineY = (int)System.Math.Abs(LineWidth.Value.y);
        int offsetX = (int)Offset.Value.x;
        int offsetY = (int)Offset.Value.y;

        // Columns first, then rows; each line spreads out from its centre one pixel to either side in turn.
        if (lineX > 0)
        {
            for (int centre = Repeat(offsetX, spacingX) - spacingX; centre < width + lineX; centre += spacingX)
            {
                for (int k = 0; k < lineX; k++)
                {
                    int column = centre + Spread(k);
                    if (column < 0 || column >= width)
                        continue;
                    for (int y = 0; y < height; y++)
                        Write(pixels, (y * width + column) * 4, line);
                }
            }
        }
        if (lineY > 0)
        {
            for (int centre = Repeat(offsetY, spacingY) - spacingY; centre < height + lineY; centre += spacingY)
            {
                for (int k = 0; k < lineY; k++)
                {
                    int row = centre + Spread(k);
                    if (row < 0 || row >= height)
                        continue;
                    for (int x = 0; x < width; x++)
                        Write(pixels, (row * width + x) * 4, line);
                }
            }
        }
        asset.SetImageData(pixels, width, height, GenerateMipmaps.Value);
    }

    // 0, +1, -1, +2, -2 ...
    private static int Spread(int k) => (k + 1) / 2 * (k % 2 == 0 ? 1 : -1);

    private static int Repeat(int value, int length) => ((value % length) + length) % length;

    private static void Write(byte[] pixels, int index, in color value)
    {
        pixels[index] = ToByte(value.r);
        pixels[index + 1] = ToByte(value.g);
        pixels[index + 2] = ToByte(value.b);
        pixels[index + 3] = ToByte(value.a);
    }

    private static byte ToByte(float value)
    {
        if (!(value > 0f)) return 0;
        if (value >= 1f) return 255;
        return (byte)(value * 255f + 0.5f);
    }
}
