// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

// THE TEXTURE SIZE CAP, AS A MEMORY CEILING.
//
// A picture is sized from its header before anything is decoded. Within the cap it decodes as it always
// has. Above it, the formats that can be read a piece at a time never exist at full size: PNG, TGA, BMP
// and PSD stream their rows into a box filter, JPEG decodes each block straight to 1/2, 1/4 or 1/8, a
// Radiance picture streams its scanlines, and a DDS or KTX with its own mips starts at the first level
// that fits. Everything else (WebP, GIF, TIFF, EXR, SVG, a file whose header lies) is decoded whole and
// then reduced, one such decode at a time in the whole process, and says so once per format. The result
// never has an edge above the cap. -xlinka
public static class CappedImageDecoder
{
    private static readonly SemaphoreSlim _wholeDecodes = new(1, 1);
    private static int _wholeWarned;
    private static int _wholeCount;

    // Pictures above the cap that had to be decoded whole first, since start.
    public static int WholeDecodes => Volatile.Read(ref _wholeCount);

    public static byte[]? DecodeRgba(byte[] encoded, int cap, out int width, out int height, out int sourceWidth, out int sourceHeight)
    {
        width = height = sourceWidth = sourceHeight = 0;
        if (cap <= 0 || !ImageHeader.TryProbe(encoded, out var container, out sourceWidth, out sourceHeight)
            || System.Math.Max(sourceWidth, sourceHeight) <= cap)
        {
            var plain = TextureVariantStore.DecodeRgba(encoded, out width, out height);
            if (sourceWidth <= 0)
            {
                sourceWidth = width;
                sourceHeight = height;
            }
            return plain != null && cap > 0 && System.Math.Max(width, height) > cap ? Shrink(plain, ref width, ref height, cap) : plain;
        }

        var (targetWidth, targetHeight) = BoxReducer.Fit(sourceWidth, sourceHeight, cap);
        byte[]? reduced = container switch
        {
            ImageContainer.Png => PngRowDecoder.DecodeReduced(encoded, targetWidth, targetHeight, out _, out _),
            ImageContainer.Jpeg => JpegScaledDecoder.DecodeReduced(encoded, targetWidth, targetHeight, out _, out _),
            ImageContainer.Tga => TgaRowDecoder.DecodeReduced(encoded, targetWidth, targetHeight),
            ImageContainer.Bmp => BmpRowDecoder.DecodeReduced(encoded, targetWidth, targetHeight),
            ImageContainer.Psd => PsdRowDecoder.DecodeReduced(encoded, targetWidth, targetHeight),
            _ => null,
        };
        if (reduced != null)
        {
            width = targetWidth;
            height = targetHeight;
            return reduced;
        }

        if (MipSlicer.TrySlice(encoded, cap, out var sliced))
        {
            var level = TextureVariantStore.DecodeRgba(sliced, out width, out height);
            if (level != null)
                return System.Math.Max(width, height) > cap ? Shrink(level, ref width, ref height, cap) : level;
        }

        Whole(container);
        _wholeDecodes.Wait();
        try
        {
            var whole = TextureVariantStore.DecodeRgba(encoded, out width, out height);
            return whole == null ? null : Shrink(whole, ref width, ref height, cap);
        }
        finally
        {
            _wholeDecodes.Release();
        }
    }

    // RGBA half floats, for a floating-point source.
    public static byte[]? DecodeRgbaHalf(byte[] encoded, int cap, out int width, out int height, out int sourceWidth, out int sourceHeight)
    {
        width = height = sourceWidth = sourceHeight = 0;
        if (cap <= 0 || !ImageHeader.TryProbe(encoded, out var container, out sourceWidth, out sourceHeight)
            || System.Math.Max(sourceWidth, sourceHeight) <= cap)
        {
            var plain = TextureVariantStore.DecodeRgbaHalf(encoded, out width, out height);
            if (sourceWidth <= 0)
            {
                sourceWidth = width;
                sourceHeight = height;
            }
            return plain != null && cap > 0 && System.Math.Max(width, height) > cap ? ShrinkHalf(plain, ref width, ref height, cap) : plain;
        }

        var (targetWidth, targetHeight) = BoxReducer.Fit(sourceWidth, sourceHeight, cap);
        if (container == ImageContainer.Radiance && RadianceRowDecoder.DecodeReducedHalf(encoded, targetWidth, targetHeight) is { } streamed)
        {
            width = targetWidth;
            height = targetHeight;
            return streamed;
        }

        Whole(container);
        _wholeDecodes.Wait();
        try
        {
            var whole = TextureVariantStore.DecodeRgbaHalf(encoded, out width, out height);
            return whole == null ? null : ShrinkHalf(whole, ref width, ref height, cap);
        }
        finally
        {
            _wholeDecodes.Release();
        }
    }

    private static void Whole(ImageContainer container)
    {
        Interlocked.Increment(ref _wholeCount);
        int bit = 1 << (int)container;
        if ((Interlocked.Or(ref _wholeWarned, bit) & bit) == 0)
            Logger.Warn($"CappedImageDecoder: a {container} picture above the texture size cap is decoded whole before it is reduced; nothing here reads that format a piece at a time");
    }

    private static byte[] Shrink(byte[] rgba, ref int width, ref int height, int cap)
    {
        var (targetWidth, targetHeight) = BoxReducer.Fit(width, height, cap);
        var reduced = BoxReducer.Reduce(rgba, width, height, targetWidth, targetHeight);
        width = targetWidth;
        height = targetHeight;
        return reduced;
    }

    private static byte[] ShrinkHalf(byte[] half, ref int width, ref int height, int cap)
    {
        var (targetWidth, targetHeight) = BoxReducer.Fit(width, height, cap);
        var result = new byte[(long)targetWidth * targetHeight * 8];
        var sums = new double[(long)targetWidth * targetHeight * 4];
        var counts = new int[(long)targetWidth * targetHeight];
        for (int y = 0; y < height; y++)
        {
            int ty = (int)((long)y * targetHeight / height);
            for (int x = 0; x < width; x++)
            {
                int tx = (int)((long)x * targetWidth / width);
                long i = ((long)y * width + x) * 8;
                long t = (long)ty * targetWidth + tx;
                for (int c = 0; c < 4; c++)
                    sums[t * 4 + c] += (float)BitConverter.UInt16BitsToHalf((ushort)(half[i + c * 2] | half[i + c * 2 + 1] << 8));
                counts[t]++;
            }
        }
        for (long t = 0; t < counts.LongLength; t++)
        {
            int n = System.Math.Max(1, counts[t]);
            for (int c = 0; c < 4; c++)
            {
                ushort bits = BitConverter.HalfToUInt16Bits((Half)(float)(sums[t * 4 + c] / n));
                result[t * 8 + c * 2] = (byte)bits;
                result[t * 8 + c * 2 + 1] = (byte)(bits >> 8);
            }
        }
        width = targetWidth;
        height = targetHeight;
        return result;
    }
}
