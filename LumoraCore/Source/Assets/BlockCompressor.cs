// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;

namespace Lumora.Core.Assets;

// BC1 and BC3 block compression on the CPU.
//
// The renderer's own encoders ship with its editor only: in an exported game every compress call returns
// "unavailable" and the picture goes to the GPU as plain RGBA8 with a mip chain, 5.3 bytes a pixel where
// BC1 is 0.67. A world of three hundred textures asked for over six gigabytes of video memory that way.
// This is the floor under that: a principal-axis fit per 4x4 block refined by least squares, solid blocks
// matched exactly through the interpolated colour, and a BC4-style alpha block for BC3. On real albedo,
// normal and cut-out maps it lands at 42 to 50 dB, two to ten above a plain bounding-box fit; speed is a few
// hundred nanoseconds a block, so a 4k texture with its mips is well under a second on one core, and
// large levels split across cores by block row.
//
// Input is RGBA8 rows top-down. Edge blocks of a level that is not a multiple of four repeat the last
// column and row. Output is the standard block layout, blocks left to right then top to bottom. -xlinka
public static class BlockCompressor
{
    public enum Format
    {
        Bc1,
        Bc3,
    }

    public static int BlockBytes(Format format) => format == Format.Bc1 ? 8 : 16;

    public static int BlocksAcross(int pixels) => (System.Math.Max(1, pixels) + 3) >> 2;

    public static long LevelBytes(int width, int height, Format format)
        => (long)BlocksAcross(width) * BlocksAcross(height) * BlockBytes(format);

    // Levels with fewer block rows than this stay on the calling thread.
    private const int ParallelRows = 32;

    // Rows of one level go to at most this many threads. Several pictures compress at once while a world or
    // an import arrives, and each one fanning out over every core held the whole thread pool and every core
    // for seconds: the frame, the physics step and anything waiting on a pool thread waited with them. -xlinka
    internal static readonly ParallelOptions RowWorkers = new() { MaxDegreeOfParallelism = System.Math.Max(1, Environment.ProcessorCount / 8) };

    // Encodes one level. perceptual weighs colour error toward green the way the eye does; leave it off
    // for data such as normal maps, where every channel counts the same.
    public static void EncodeLevel(byte[] source, int sourceOffset, int width, int height, Format format, bool perceptual,
        byte[] destination, int destinationOffset)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (sourceOffset < 0 || (long)sourceOffset + (long)width * height * 4 > source.LongLength)
            throw new ArgumentException("Source is smaller than the level.", nameof(source));
        if (destinationOffset < 0 || destinationOffset + LevelBytes(width, height, format) > destination.LongLength)
            throw new ArgumentException("Destination is smaller than the level.", nameof(destination));

        int rows = BlocksAcross(height);
        if (rows < ParallelRows || Environment.ProcessorCount == 1)
        {
            for (int row = 0; row < rows; row++)
                EncodeRow(source, sourceOffset, width, height, row, format, perceptual, destination, destinationOffset);
            return;
        }

        Parallel.For(0, rows, RowWorkers, row =>
            EncodeRow(source, sourceOffset, width, height, row, format, perceptual, destination, destinationOffset));
    }

    private static void EncodeRow(byte[] source, int sourceOffset, int width, int height, int row, Format format,
        bool perceptual, byte[] destination, int destinationOffset)
    {
        Span<int> pixels = stackalloc int[64];
        int across = BlocksAcross(width);
        int blockBytes = BlockBytes(format);
        int output = destinationOffset + row * across * blockBytes;

        for (int column = 0; column < across; column++)
        {
            for (int y = 0; y < 4; y++)
            {
                int sy = System.Math.Min(row * 4 + y, height - 1);
                int line = sourceOffset + sy * width * 4;
                for (int x = 0; x < 4; x++)
                {
                    int sx = System.Math.Min(column * 4 + x, width - 1);
                    int at = line + sx * 4;
                    int p = (y * 4 + x) * 4;
                    pixels[p] = source[at];
                    pixels[p + 1] = source[at + 1];
                    pixels[p + 2] = source[at + 2];
                    pixels[p + 3] = source[at + 3];
                }
            }

            var block = destination.AsSpan(output, blockBytes);
            if (format == Format.Bc3)
            {
                EncodeAlpha(pixels, block.Slice(0, 8));
                EncodeColor(pixels, perceptual, block.Slice(8, 8));
            }
            else
            {
                EncodeColor(pixels, perceptual, block);
            }
            output += blockBytes;
        }
    }

    // COLOUR BLOCK

    // Endpoint pairs whose two-thirds point lands exactly on each 8-bit value, for blocks of one colour:
    // two quantised endpoints reach far more values between them than either does alone.
    private static readonly byte[] Solid5 = BuildSolidTable(5);
    private static readonly byte[] Solid6 = BuildSolidTable(6);

    private static byte[] BuildSolidTable(int bits)
    {
        int levels = 1 << bits;
        var table = new byte[256 * 2];
        for (int value = 0; value < 256; value++)
        {
            int bestError = int.MaxValue;
            for (int a = 0; a < levels; a++)
            {
                int ea = Expand(a, bits);
                for (int b = 0; b < levels; b++)
                {
                    int eb = Expand(b, bits);
                    int error = System.Math.Abs((2 * ea + eb + 1) / 3 - value) * 1024 + System.Math.Abs(ea - eb);
                    if (error < bestError)
                    {
                        bestError = error;
                        table[value * 2] = (byte)a;
                        table[value * 2 + 1] = (byte)b;
                    }
                }
            }
        }
        return table;
    }

    private static int Expand(int value, int bits) => bits == 5 ? (value << 3) | (value >> 2) : (value << 2) | (value >> 4);

    private static int Pack565(int r, int g, int b) => (r << 11) | (g << 5) | b;

    private static int Quantize(float value, int bits)
    {
        int max = (1 << bits) - 1;
        int q = (int)(System.Math.Clamp(value, 0f, 255f) * max / 255f + 0.5f);
        return System.Math.Clamp(q, 0, max);
    }

    private static void EncodeColor(Span<int> pixels, bool perceptual, Span<byte> output)
    {
        int r0 = pixels[0], g0 = pixels[1], b0 = pixels[2];
        bool solid = true;
        for (int i = 1; i < 16 && solid; i++)
            solid = pixels[i * 4] == r0 && pixels[i * 4 + 1] == g0 && pixels[i * 4 + 2] == b0;
        if (solid)
        {
            int high = Pack565(Solid5[r0 * 2], Solid6[g0 * 2], Solid5[b0 * 2]);
            int low = Pack565(Solid5[r0 * 2 + 1], Solid6[g0 * 2 + 1], Solid5[b0 * 2 + 1]);
            // Every texel takes the two-thirds point; with the ends swapped that is index 3 instead.
            uint indices = 0xAAAAAAAAu;
            if (high < low)
            {
                (high, low) = (low, high);
                indices = 0xFFFFFFFFu;
            }
            else if (high == low)
            {
                indices = 0;
            }
            WriteColor(output, high, low, indices);
            return;
        }

        float wr = perceptual ? 0.299f : 1f, wg = perceptual ? 0.587f : 1f, wb = perceptual ? 0.114f : 1f;
        float sr = MathF.Sqrt(wr), sg = MathF.Sqrt(wg), sb = MathF.Sqrt(wb);

        // Principal axis of the block in the weighted space, by power iteration on its covariance.
        float mr = 0f, mg = 0f, mb = 0f;
        for (int i = 0; i < 16; i++)
        {
            mr += pixels[i * 4] * sr;
            mg += pixels[i * 4 + 1] * sg;
            mb += pixels[i * 4 + 2] * sb;
        }
        mr /= 16f;
        mg /= 16f;
        mb /= 16f;

        float crr = 0f, crg = 0f, crb = 0f, cgg = 0f, cgb = 0f, cbb = 0f;
        for (int i = 0; i < 16; i++)
        {
            float dr = pixels[i * 4] * sr - mr;
            float dg = pixels[i * 4 + 1] * sg - mg;
            float db = pixels[i * 4 + 2] * sb - mb;
            crr += dr * dr;
            crg += dr * dg;
            crb += dr * db;
            cgg += dg * dg;
            cgb += dg * db;
            cbb += db * db;
        }

        float vr = 1f, vg = 1f, vb = 1f;
        for (int iteration = 0; iteration < 6; iteration++)
        {
            float nr = crr * vr + crg * vg + crb * vb;
            float ng = crg * vr + cgg * vg + cgb * vb;
            float nb = crb * vr + cgb * vg + cbb * vb;
            float largest = System.Math.Max(MathF.Abs(nr), System.Math.Max(MathF.Abs(ng), MathF.Abs(nb)));
            if (largest < 1e-6f)
                break;
            vr = nr / largest;
            vg = ng / largest;
            vb = nb / largest;
        }
        float length = MathF.Sqrt(vr * vr + vg * vg + vb * vb);
        if (length < 1e-6f)
        {
            vr = sr;
            vg = sg;
            vb = sb;
            length = MathF.Sqrt(vr * vr + vg * vg + vb * vb);
        }
        vr /= length;
        vg /= length;
        vb /= length;

        float tMin = float.MaxValue, tMax = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float t = (pixels[i * 4] * sr - mr) * vr + (pixels[i * 4 + 1] * sg - mg) * vg + (pixels[i * 4 + 2] * sb - mb) * vb;
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
        }

        // Back out of the weighted space to plain 8-bit endpoints.
        float hr = (mr + vr * tMax) / sr, hg = (mg + vg * tMax) / sg, hb = (mb + vb * tMax) / sb;
        float lr = (mr + vr * tMin) / sr, lg = (mg + vg * tMin) / sg, lb = (mb + vb * tMin) / sb;

        int bestHigh = Pack565(Quantize(hr, 5), Quantize(hg, 6), Quantize(hb, 5));
        int bestLow = Pack565(Quantize(lr, 5), Quantize(lg, 6), Quantize(lb, 5));
        uint bestIndices = Fit(pixels, bestHigh, bestLow, wr, wg, wb, out float bestError);

        // Least squares on the endpoints for the indices just chosen, twice; kept only when better.
        for (int pass = 0; pass < 2; pass++)
        {
            if (!Refine(pixels, bestIndices, out int high, out int low))
                break;
            if (high == bestHigh && low == bestLow)
                break;
            uint indices = Fit(pixels, high, low, wr, wg, wb, out float error);
            if (error >= bestError)
                break;
            bestHigh = high;
            bestLow = low;
            bestIndices = indices;
            bestError = error;
        }

        // Four-colour mode needs the first endpoint strictly greater. Swapping the ends swaps 0 with 1
        // and 2 with 3, which is flipping the low bit of every index.
        if (bestHigh < bestLow)
        {
            (bestHigh, bestLow) = (bestLow, bestHigh);
            bestIndices ^= 0x55555555u;
        }
        else if (bestHigh == bestLow)
        {
            bestIndices = 0;
        }
        WriteColor(output, bestHigh, bestLow, bestIndices);
    }

    // Nearest palette entry per texel under the channel weights, and the total weighted error. The palette
    // is the four-colour one whichever way round the endpoints are; ordering is fixed up afterwards.
    private static uint Fit(Span<int> pixels, int high, int low, float wr, float wg, float wb, out float error)
    {
        int ar = Expand((high >> 11) & 31, 5), ag = Expand((high >> 5) & 63, 6), ab = Expand(high & 31, 5);
        int br = Expand((low >> 11) & 31, 5), bg = Expand((low >> 5) & 63, 6), bb = Expand(low & 31, 5);
        Span<int> palette = stackalloc int[12];
        palette[0] = ar; palette[1] = ag; palette[2] = ab;
        palette[3] = br; palette[4] = bg; palette[5] = bb;
        palette[6] = (2 * ar + br + 1) / 3; palette[7] = (2 * ag + bg + 1) / 3; palette[8] = (2 * ab + bb + 1) / 3;
        palette[9] = (ar + 2 * br + 1) / 3; palette[10] = (ag + 2 * bg + 1) / 3; palette[11] = (ab + 2 * bb + 1) / 3;

        uint indices = 0;
        error = 0f;
        for (int i = 0; i < 16; i++)
        {
            int r = pixels[i * 4], g = pixels[i * 4 + 1], b = pixels[i * 4 + 2];
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float dr = r - palette[k * 3], dg = g - palette[k * 3 + 1], db = b - palette[k * 3 + 2];
                float distance = wr * dr * dr + wg * dg * dg + wb * db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = k;
                }
            }
            indices |= (uint)best << (i * 2);
            error += bestDistance;
        }
        return indices;
    }

    // Endpoints that minimise the squared error for a fixed index choice, per channel.
    private static bool Refine(Span<int> pixels, uint indices, out int high, out int low)
    {
        float aa = 0f, ab = 0f, bb = 0f;
        float xr = 0f, xg = 0f, xb = 0f, yr = 0f, yg = 0f, yb = 0f;
        for (int i = 0; i < 16; i++)
        {
            float alpha = ((indices >> (i * 2)) & 3) switch
            {
                0 => 1f,
                1 => 0f,
                2 => 2f / 3f,
                _ => 1f / 3f,
            };
            float beta = 1f - alpha;
            aa += alpha * alpha;
            ab += alpha * beta;
            bb += beta * beta;
            int r = pixels[i * 4], g = pixels[i * 4 + 1], b = pixels[i * 4 + 2];
            xr += alpha * r; xg += alpha * g; xb += alpha * b;
            yr += beta * r; yg += beta * g; yb += beta * b;
        }

        float det = aa * bb - ab * ab;
        if (MathF.Abs(det) < 1e-6f)
        {
            high = low = 0;
            return false;
        }
        float inv = 1f / det;
        high = Pack565(Quantize((bb * xr - ab * yr) * inv, 5), Quantize((bb * xg - ab * yg) * inv, 6), Quantize((bb * xb - ab * yb) * inv, 5));
        low = Pack565(Quantize((aa * yr - ab * xr) * inv, 5), Quantize((aa * yg - ab * xg) * inv, 6), Quantize((aa * yb - ab * xb) * inv, 5));
        return true;
    }

    private static void WriteColor(Span<byte> output, int high, int low, uint indices)
    {
        output[0] = (byte)high;
        output[1] = (byte)(high >> 8);
        output[2] = (byte)low;
        output[3] = (byte)(low >> 8);
        output[4] = (byte)indices;
        output[5] = (byte)(indices >> 8);
        output[6] = (byte)(indices >> 16);
        output[7] = (byte)(indices >> 24);
    }

    // ALPHA BLOCK
    //
    // Eight-step ramp between the block's extremes, or, when the block holds exact 0 or 255 among other
    // values, a six-step ramp between the rest with 0 and 255 as their own entries; whichever errs less.
    // A cut-out edge is mostly 0 and 255, which is exactly the case the second form exists for.
    private static void EncodeAlpha(Span<int> pixels, Span<byte> output)
    {
        int min = 255, max = 0;
        int innerMin = 255, innerMax = 0;
        bool hasExtremes = false;
        for (int i = 0; i < 16; i++)
        {
            int a = pixels[i * 4 + 3];
            if (a < min) min = a;
            if (a > max) max = a;
            if (a == 0 || a == 255)
            {
                hasExtremes = true;
                continue;
            }
            if (a < innerMin) innerMin = a;
            if (a > innerMax) innerMax = a;
        }

        if (min == max)
        {
            output.Clear();
            output[0] = (byte)min;
            output[1] = (byte)min;
            return;
        }

        Span<int> palette = stackalloc int[8];
        palette[0] = max;
        palette[1] = min;
        for (int i = 1; i < 7; i++)
            palette[i + 1] = ((7 - i) * max + i * min + 3) / 7;
        ulong eightIndices = FitAlpha(pixels, palette, out int eightError);

        if (hasExtremes)
        {
            if (innerMin > innerMax)
            {
                innerMin = 0;
                innerMax = 255;
            }
            palette[0] = innerMin;
            palette[1] = innerMax;
            for (int i = 1; i < 5; i++)
                palette[i + 1] = ((5 - i) * innerMin + i * innerMax + 2) / 5;
            palette[6] = 0;
            palette[7] = 255;
            ulong sixIndices = FitAlpha(pixels, palette, out int sixError);
            if (sixError < eightError)
            {
                WriteAlpha(output, innerMin, innerMax, sixIndices);
                return;
            }
        }

        WriteAlpha(output, max, min, eightIndices);
    }

    private static ulong FitAlpha(Span<int> pixels, Span<int> palette, out int error)
    {
        ulong indices = 0;
        error = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = pixels[i * 4 + 3];
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int d = a - palette[k];
                d *= d;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = k;
                }
            }
            indices |= (ulong)best << (i * 3);
            error += bestDistance;
        }
        return indices;
    }

    private static void WriteAlpha(Span<byte> output, int first, int second, ulong indices)
    {
        output[0] = (byte)first;
        output[1] = (byte)second;
        for (int i = 0; i < 6; i++)
            output[2 + i] = (byte)(indices >> (i * 8));
    }
}
