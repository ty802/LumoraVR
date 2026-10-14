// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// What the asset nodes read: the asset a provider the script can see has loaded on this machine.
// Pixels are this machine's copy, rows from the top, each mip level half the size of the one before. A
// loaded picture hands its pixels to the renderer and keeps none, so the first read decodes them again
// from its file off the world thread and reads as nothing until they land; reads after that find them
// while the script keeps reading. A level has to match its size to be read at all. -xlinka
internal static class ScriptAssets
{
    public static TextureAsset? Texture(ScriptContext ctx, IAssetProvider<TextureAsset>? provider)
        => provider is Component component && ScriptReads.Visible(ctx, component) ? provider.Asset : null;

    public static AnimationClip? Clip(ScriptContext ctx, IAssetProvider<AnimationAsset>? provider)
        => provider is Component component && ScriptReads.Visible(ctx, component) ? provider.Asset?.Clip : null;

    public static bool TryLevel(TextureAsset? texture, int mip, out Level level)
    {
        level = default;
        if (texture == null || mip < 0 || mip >= 31)
            return false;
        var pixels = texture.TryGetPixels();
        if (pixels == null)
            return false;
        var levels = pixels.Levels;
        int width = pixels.Width;
        int height = pixels.Height;
        bool half = pixels.IsHalf;
        if (mip >= levels.Length || width <= 0 || height <= 0)
            return false;
        int levelWidth = System.Math.Max(1, width >> mip);
        int levelHeight = System.Math.Max(1, height >> mip);
        var bytes = levels[mip];
        if (bytes == null || bytes.LongLength != (long)levelWidth * levelHeight * (half ? 8 : 4))
            return false;
        level = new Level(bytes, levelWidth, levelHeight, half);
        return true;
    }

    public readonly struct Level
    {
        private readonly byte[] _bytes;

        public Level(byte[] bytes, int width, int height, bool half)
        {
            _bytes = bytes;
            Width = width;
            Height = height;
            Half = half;
        }

        public int Width { get; }
        public int Height { get; }

        // Half floats rather than bytes.
        public bool Half { get; }

        public color Texel(int x, int y)
        {
            int index = (y * Width + x) * (Half ? 8 : 4);
            if (Half)
            {
                return new color((float)BitConverter.ToHalf(_bytes, index), (float)BitConverter.ToHalf(_bytes, index + 2),
                    (float)BitConverter.ToHalf(_bytes, index + 4), (float)BitConverter.ToHalf(_bytes, index + 6));
            }
            return new color(_bytes[index] / 255f, _bytes[index + 1] / 255f, _bytes[index + 2] / 255f, _bytes[index + 3] / 255f);
        }

        // Blended from the four texels round uv, with texel centres at half steps.
        public color Sample(float2 uv, TextureWrapMode wrap)
        {
            double x = Wrapped(uv.x, wrap) * Width - 0.5;
            double y = Wrapped(uv.y, wrap) * Height - 0.5;
            int x0 = (int)System.Math.Floor(x);
            int y0 = (int)System.Math.Floor(y);
            float across = (float)(x - x0);
            float down = (float)(y - y0);
            var top = color.Lerp(At(x0, y0, wrap), At(x0 + 1, y0, wrap), across);
            var bottom = color.Lerp(At(x0, y0 + 1, wrap), At(x0 + 1, y0 + 1, wrap), across);
            return color.Lerp(top, bottom, down);
        }

        // Brought into the span each wrap repeats over, so no coordinate is too big to count in texels.
        private static double Wrapped(float t, TextureWrapMode wrap) => wrap switch
        {
            TextureWrapMode.Repeat => t - System.Math.Floor(t),
            TextureWrapMode.Mirror => t - 2.0 * System.Math.Floor(t / 2.0),
            _ => System.Math.Clamp(t, -1.0, 2.0),
        };

        private color At(int x, int y, TextureWrapMode wrap)
        {
            switch (wrap)
            {
                case TextureWrapMode.Repeat:
                    return Texel(Repeat(x, Width), Repeat(y, Height));
                case TextureWrapMode.Mirror:
                    return Texel(Mirror(x, Width), Mirror(y, Height));
                case TextureWrapMode.ClampToBorder:
                    return x < 0 || y < 0 || x >= Width || y >= Height ? default : Texel(x, y);
                default:
                    return Texel(System.Math.Clamp(x, 0, Width - 1), System.Math.Clamp(y, 0, Height - 1));
            }
        }

        private static int Repeat(int value, int size)
        {
            int wrapped = value % size;
            return wrapped < 0 ? wrapped + size : wrapped;
        }

        private static int Mirror(int value, int size)
        {
            int wrapped = Repeat(value, size * 2);
            return wrapped < size ? wrapped : size * 2 - 1 - wrapped;
        }
    }
}
