// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;

namespace Lumora.Core.Assets;

// ETC2 block compression on the CPU, for the headsets and phones whose GPUs sample ETC2 and whose exported
// build has no encoder of the platform's own: there every texture went up as RGBA8, four times the memory,
// in a device where texture memory is the same memory the whole app runs in.
//
// ETC2 RGB for opaque colour and normal maps (4 bits a pixel), ETC2 RGBA8 for colour with alpha (an EAC
// alpha block in front of the colour block, 8 bits a pixel). Each colour block tries both splits in
// individual and differential mode with all eight tables around each half's average, and the planar
// mode that smooth gradients need; the alpha block searches every table near the multiplier its range
// asks for. ASTC is not offered: a
// CPU encoder for it that is both fast and good is a far larger piece of work, and ETC2 is the format
// every GLES3 and Vulkan device must sample.
//
// Input is RGBA8 rows top-down; edge blocks repeat the last row and column. Output is the standard block
// order, blocks left to right then top to bottom, each block's 64-bit words big-endian. -xlinka
public static class Etc2Compressor
{
    public enum Format
    {
        Etc2Rgb,
        Etc2Rgba,
    }

    public static int BlockBytes(Format format) => format == Format.Etc2Rgb ? 8 : 16;

    public static int BlocksAcross(int pixels) => (System.Math.Max(1, pixels) + 3) >> 2;

    public static long LevelBytes(int width, int height, Format format)
        => (long)BlocksAcross(width) * BlocksAcross(height) * BlockBytes(format);

    private const int ParallelRows = 16;

    private static readonly int[,] Tables =
    {
        { 2, 8 }, { 5, 17 }, { 9, 29 }, { 13, 42 }, { 18, 60 }, { 24, 80 }, { 33, 106 }, { 47, 183 },
    };

    private static readonly int[,] AlphaTables =
    {
        { -3, -6, -9, -15, 2, 5, 8, 14 }, { -3, -7, -10, -13, 2, 6, 9, 12 }, { -2, -5, -8, -13, 1, 4, 7, 12 },
        { -2, -4, -6, -13, 1, 3, 5, 12 }, { -3, -6, -8, -12, 2, 5, 7, 11 }, { -3, -7, -9, -11, 2, 6, 8, 10 },
        { -4, -7, -8, -11, 3, 6, 7, 10 }, { -3, -5, -8, -11, 2, 4, 7, 10 }, { -2, -6, -8, -10, 1, 5, 7, 9 },
        { -2, -5, -8, -10, 1, 4, 7, 9 }, { -2, -4, -8, -10, 1, 3, 7, 9 }, { -2, -5, -7, -10, 1, 4, 6, 9 },
        { -3, -4, -7, -10, 2, 3, 6, 9 }, { -1, -2, -3, -10, 0, 1, 2, 9 }, { -4, -6, -8, -9, 3, 5, 7, 8 },
        { -3, -5, -7, -9, 2, 4, 6, 8 },
    };

    public static void EncodeLevel(byte[] source, int sourceOffset, int width, int height, Format format, byte[] destination, int destinationOffset)
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
                EncodeRow(source, sourceOffset, width, height, row, format, destination, destinationOffset);
            return;
        }
        Parallel.For(0, rows, BlockCompressor.RowWorkers, row => EncodeRow(source, sourceOffset, width, height, row, format, destination, destinationOffset));
    }

    private static void EncodeRow(byte[] source, int sourceOffset, int width, int height, int row, Format format, byte[] destination, int destinationOffset)
    {
        Span<int> pixels = stackalloc int[64];
        int across = BlocksAcross(width);
        int blockBytes = BlockBytes(format);
        int output = destinationOffset + row * across * blockBytes;
        for (int column = 0; column < across; column++)
        {
            // Pixel p of a block is at x = p / 4, y = p % 4: the order the index bits are in.
            for (int x = 0; x < 4; x++)
            {
                int sx = System.Math.Min(column * 4 + x, width - 1);
                for (int y = 0; y < 4; y++)
                {
                    int sy = System.Math.Min(row * 4 + y, height - 1);
                    int at = sourceOffset + (sy * width + sx) * 4;
                    int p = (x * 4 + y) * 4;
                    pixels[p] = source[at];
                    pixels[p + 1] = source[at + 1];
                    pixels[p + 2] = source[at + 2];
                    pixels[p + 3] = source[at + 3];
                }
            }
            var block = destination.AsSpan(output, blockBytes);
            if (format == Format.Etc2Rgba)
            {
                EncodeAlpha(pixels, block.Slice(0, 8));
                EncodeColor(pixels, block.Slice(8, 8));
            }
            else
                EncodeColor(pixels, block);
            output += blockBytes;
        }
    }

    // COLOUR

    private static void EncodeColor(ReadOnlySpan<int> pixels, Span<byte> block)
    {
        ulong best = 0;
        long bestError = long.MaxValue;
        Span<int> half0 = stackalloc int[8];
        Span<int> half1 = stackalloc int[8];

        for (int flip = 0; flip < 2; flip++)
        {
            int n0 = 0, n1 = 0;
            for (int p = 0; p < 16; p++)
            {
                int x = p >> 2, y = p & 3;
                bool second = flip == 0 ? x >= 2 : y >= 2;
                if (second) half1[n1++] = p;
                else half0[n0++] = p;
            }

            Average(pixels, half0, out float r0, out float g0, out float b0);
            Average(pixels, half1, out float r1, out float g1, out float b1);

            // Differential: 5-bit base and a 3-bit delta, when the halves are close enough.
            int qr0 = Quant(r0, 31), qg0 = Quant(g0, 31), qb0 = Quant(b0, 31);
            int qr1 = Quant(r1, 31), qg1 = Quant(g1, 31), qb1 = Quant(b1, 31);
            int dr = qr1 - qr0, dg = qg1 - qg0, db = qb1 - qb0;
            if (dr >= -4 && dr <= 3 && dg >= -4 && dg <= 3 && db >= -4 && db <= 3)
            {
                long error = 0;
                var (t0, i0, e0) = BestHalf(pixels, half0, Expand5(qr0), Expand5(qg0), Expand5(qb0));
                var (t1, i1, e1) = BestHalf(pixels, half1, Expand5(qr1), Expand5(qg1), Expand5(qb1));
                error = e0 + e1;
                if (error < bestError)
                {
                    bestError = error;
                    best = PackColor(true, flip, qr0, qg0, qb0, dr & 7, dg & 7, db & 7, t0, t1, i0 | i1);
                }
            }

            // Individual: two independent 4-bit colours.
            {
                int ar0 = Quant(r0, 15), ag0 = Quant(g0, 15), ab0 = Quant(b0, 15);
                int ar1 = Quant(r1, 15), ag1 = Quant(g1, 15), ab1 = Quant(b1, 15);
                var (t0, i0, e0) = BestHalf(pixels, half0, ar0 * 17, ag0 * 17, ab0 * 17);
                var (t1, i1, e1) = BestHalf(pixels, half1, ar1 * 17, ag1 * 17, ab1 * 17);
                long error = e0 + e1;
                if (error < bestError)
                {
                    bestError = error;
                    best = PackColor(false, flip, ar0, ag0, ab0, ar1, ag1, ab1, t0, t1, i0 | i1);
                }
            }
            if (bestError == 0)
                break;
        }

        if (bestError > 0)
        {
            var (planar, planarError) = Planar(pixels);
            if (planarError < bestError)
                best = planar;
        }

        for (int i = 0; i < 8; i++)
            block[i] = (byte)(best >> (56 - i * 8));
    }

    private static void Average(ReadOnlySpan<int> pixels, ReadOnlySpan<int> half, out float r, out float g, out float b)
    {
        int sr = 0, sg = 0, sb = 0;
        foreach (int p in half)
        {
            sr += pixels[p * 4];
            sg += pixels[p * 4 + 1];
            sb += pixels[p * 4 + 2];
        }
        r = sr / 8f;
        g = sg / 8f;
        b = sb / 8f;
    }

    private static int Quant(float value, int max) => System.Math.Clamp((int)MathF.Round(value * max / 255f), 0, max);

    private static int Expand5(int v) => (v << 3) | (v >> 2);

    // The best table and indices for one half with its base colour, and the error.
    private static (int Table, uint Indices, long Error) BestHalf(ReadOnlySpan<int> pixels, ReadOnlySpan<int> half, int br, int bg, int bb)
    {
        int bestTable = 0;
        uint bestIndices = 0;
        long bestError = long.MaxValue;
        for (int t = 0; t < 8; t++)
        {
            int small = Tables[t, 0], large = Tables[t, 1];
            long error = 0;
            uint indices = 0;
            foreach (int p in half)
            {
                int r = pixels[p * 4], g = pixels[p * 4 + 1], b = pixels[p * 4 + 2];
                int chosen = 0;
                long chosenError = long.MaxValue;
                for (int m = 0; m < 4; m++)
                {
                    int offset = m switch { 0 => small, 1 => large, 2 => -small, _ => -large };
                    int dr = r - Clamp(br + offset), dg = g - Clamp(bg + offset), db = b - Clamp(bb + offset);
                    long e = dr * dr + dg * dg + db * db;
                    if (e < chosenError)
                    {
                        chosenError = e;
                        chosen = m;
                    }
                }
                error += chosenError;
                if (error >= bestError)
                    break;
                // MSB of the index in bit 16 + p, LSB in bit p.
                indices |= (uint)((chosen >> 1) << (16 + p)) | (uint)((chosen & 1) << p);
            }
            if (error < bestError)
            {
                bestError = error;
                bestTable = t;
                bestIndices = indices;
            }
        }
        return (bestTable, bestIndices, bestError);
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    private static ulong PackColor(bool differential, int flip, int a0, int a1, int a2, int b0, int b1, int b2, int table0, int table1, uint indices)
    {
        ulong word;
        if (differential)
            word = (ulong)a0 << 59 | (ulong)b0 << 56 | (ulong)a1 << 51 | (ulong)b1 << 48 | (ulong)a2 << 43 | (ulong)b2 << 40;
        else
            word = (ulong)a0 << 60 | (ulong)b0 << 56 | (ulong)a1 << 52 | (ulong)b1 << 48 | (ulong)a2 << 44 | (ulong)b2 << 40;
        word |= (ulong)table0 << 37 | (ulong)table1 << 34 | (differential ? 1UL : 0UL) << 33 | (ulong)flip << 32 | indices;
        return word;
    }

    // PLANAR: three corner colours, every pixel interpolated between them.
    private static (ulong Word, long Error) Planar(ReadOnlySpan<int> pixels)
    {
        Span<int> o = stackalloc int[3];
        Span<int> h = stackalloc int[3];
        Span<int> v = stackalloc int[3];
        for (int c = 0; c < 3; c++)
        {
            float mean = 0, sx = 0, sy = 0;
            for (int p = 0; p < 16; p++)
                mean += pixels[p * 4 + c];
            mean /= 16f;
            for (int p = 0; p < 16; p++)
            {
                float d = pixels[p * 4 + c] - mean;
                sx += d * ((p >> 2) - 1.5f);
                sy += d * ((p & 3) - 1.5f);
            }
            float slopeX = sx / 20f, slopeY = sy / 20f;
            float origin = mean - 1.5f * slopeX - 1.5f * slopeY;
            int bits = c == 1 ? 7 : 6;
            int max = (1 << bits) - 1;
            o[c] = System.Math.Clamp((int)MathF.Round(origin * max / 255f), 0, max);
            h[c] = System.Math.Clamp((int)MathF.Round((origin + 4 * slopeX) * max / 255f), 0, max);
            v[c] = System.Math.Clamp((int)MathF.Round((origin + 4 * slopeY) * max / 255f), 0, max);
        }

        long error = 0;
        for (int c = 0; c < 3; c++)
        {
            int bits = c == 1 ? 7 : 6;
            int eo = ExpandBits(o[c], bits), eh = ExpandBits(h[c], bits), ev = ExpandBits(v[c], bits);
            for (int p = 0; p < 16; p++)
            {
                int x = p >> 2, y = p & 3;
                int value = Clamp((x * (eh - eo) + y * (ev - eo) + 4 * eo + 2) >> 2);
                int d = pixels[p * 4 + c] - value;
                error += d * d;
            }
        }

        int ro = o[0], go = o[1], bo = o[2];
        var b = new byte[8];
        b[0] = (byte)((ro & 0x3F) << 1 | (go >> 6 & 1));
        b[1] = (byte)((go & 0x3F) << 1 | (bo >> 5 & 1));
        b[2] = (byte)((bo & 0x18) | (bo >> 1 & 0x3));
        b[3] = (byte)((bo & 1) << 7 | (h[0] >> 1 & 0x1F) << 2 | 0x2 | (h[0] & 1));
        b[4] = (byte)((h[1] & 0x7F) << 1 | (h[2] >> 5 & 1));
        b[5] = (byte)((h[2] & 0x1F) << 3 | (v[0] >> 3 & 0x7));
        b[6] = (byte)((v[0] & 0x7) << 5 | (v[1] >> 2 & 0x1F));
        b[7] = (byte)((v[1] & 0x3) << 6 | (v[2] & 0x3F));

        // The spare bits make the decoder see planar: red and green in range, blue out of it.
        int rField = b[0] >> 3, rDelta = SignExtend3(b[0] & 7);
        if (rField + rDelta < 0 || rField + rDelta > 31)
            b[0] |= 0x80;
        int gField = b[1] >> 3, gDelta = SignExtend3(b[1] & 7);
        if (gField + gDelta < 0 || gField + gDelta > 31)
            b[1] |= 0x80;
        int sum = (bo >> 3 & 3) + (bo >> 1 & 3);
        if (sum >= 4)
            b[2] |= 0xE0;
        else
            b[2] |= 0x04;

        ulong word = 0;
        for (int i = 0; i < 8; i++)
            word = word << 8 | b[i];
        return (word, error);
    }

    private static int SignExtend3(int v) => v >= 4 ? v - 8 : v;

    private static int ExpandBits(int value, int bits) => bits == 6 ? (value << 2) | (value >> 4) : (value << 1) | (value >> 6);

    // ALPHA (EAC)

    private static void EncodeAlpha(ReadOnlySpan<int> pixels, Span<byte> block)
    {
        int min = 255, max = 0;
        for (int p = 0; p < 16; p++)
        {
            int a = pixels[p * 4 + 3];
            if (a < min) min = a;
            if (a > max) max = a;
        }

        ulong bestWord = 0;
        long bestError = long.MaxValue;
        if (min == max)
        {
            // Table 13 has a zero offset at index 4: every pixel exact.
            bestWord = (ulong)min << 56 | 1UL << 52 | 13UL << 48;
            ulong indices = 0;
            for (int p = 0; p < 16; p++)
                indices |= 4UL << (45 - p * 3);
            bestWord |= indices;
        }
        else
        {
            for (int t = 0; t < 16; t++)
            {
                int low = AlphaTables[t, 3], high = AlphaTables[t, 7];
                int span = high - low;
                int ideal = System.Math.Max(1, (max - min + span - 1) / span);
                for (int m = System.Math.Min(15, ideal); m <= System.Math.Min(15, ideal + 1); m++)
                {
                    int center = (min + max + 1) / 2 - (low + high) * m / 2;
                    for (int baseOffset = -1; baseOffset <= 1; baseOffset++)
                    {
                        int baseValue = System.Math.Clamp(center + baseOffset, 0, 255);
                        long error = 0;
                        ulong indices = 0;
                        for (int p = 0; p < 16 && error < bestError; p++)
                        {
                            int a = pixels[p * 4 + 3];
                            int chosen = 0;
                            int chosenError = int.MaxValue;
                            for (int i = 0; i < 8; i++)
                            {
                                int value = Clamp(baseValue + AlphaTables[t, i] * m);
                                int d = a - value;
                                int e = d * d;
                                if (e < chosenError)
                                {
                                    chosenError = e;
                                    chosen = i;
                                }
                            }
                            error += chosenError;
                            indices |= (ulong)chosen << (45 - p * 3);
                        }
                        if (error < bestError)
                        {
                            bestError = error;
                            bestWord = (ulong)baseValue << 56 | (ulong)m << 52 | (ulong)t << 48 | indices;
                        }
                    }
                }
                if (bestError == 0)
                    break;
            }
        }
        for (int i = 0; i < 8; i++)
            block[i] = (byte)(bestWord >> (56 - i * 8));
    }
}
