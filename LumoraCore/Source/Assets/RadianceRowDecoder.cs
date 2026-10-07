// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

// A Radiance picture decoded a scanline at a time straight to a reduced RGBA half-float picture, so a
// panorama or light map bigger than the size cap is never whole in memory. Flat and run-length scanlines
// in the usual top-down orientation; the averaging is done in linear light, which is what these values
// are. Null for anything else. -xlinka
public static class RadianceRowDecoder
{
    public static byte[]? DecodeReducedHalf(byte[] hdr, int width, int height)
    {
        try
        {
            return Decode(hdr, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? Decode(byte[] d, int targetWidth, int targetHeight)
    {
        int at = 0;
        bool blank = false;
        int line = 0;
        string? size = null;
        for (; at < d.Length; at++)
        {
            if (d[at] != '\n')
                continue;
            if (blank)
            {
                size = System.Text.Encoding.ASCII.GetString(d, line, at - line).Trim();
                at++;
                break;
            }
            blank = at == line;
            line = at + 1;
        }
        if (size == null || !size.StartsWith("-Y ") || !size.Contains(" +X ")
            || !ImageHeader.ParseRadianceSize(size, out int w, out int h))
            return null;

        var sums = new double[targetWidth * 3];
        var columnOf = new int[w];
        var columnCount = new int[targetWidth];
        for (int x = 0; x < w; x++)
        {
            columnOf[x] = (int)((long)x * targetWidth / w);
            columnCount[columnOf[x]]++;
        }
        var rowCount = new int[targetHeight];
        for (int y = 0; y < h; y++)
            rowCount[(int)((long)y * targetHeight / h)]++;

        var result = new byte[(long)targetWidth * targetHeight * 8];
        var scan = new byte[w * 4];
        int openRow = -1;
        for (int y = 0; y < h; y++)
        {
            at = ReadScanline(d, at, w, scan);
            if (at < 0)
                return null;
            int row = (int)((long)y * targetHeight / h);
            if (row != openRow)
            {
                if (openRow >= 0)
                    Flush(sums, result, openRow, targetWidth, columnCount, rowCount[openRow]);
                openRow = row;
            }
            for (int x = 0; x < w; x++)
            {
                int e = scan[x * 4 + 3];
                if (e == 0)
                    continue;
                double f = System.Math.ScaleB(1.0, e - (128 + 8));
                int o = columnOf[x] * 3;
                sums[o] += scan[x * 4] * f;
                sums[o + 1] += scan[x * 4 + 1] * f;
                sums[o + 2] += scan[x * 4 + 2] * f;
            }
        }
        if (openRow >= 0)
            Flush(sums, result, openRow, targetWidth, columnCount, rowCount[openRow]);
        return result;
    }

    private static void Flush(double[] sums, byte[] result, int row, int width, int[] columnCount, int rows)
    {
        long o = (long)row * width * 8;
        for (int x = 0; x < width; x++, o += 8)
        {
            double n = System.Math.Max(1, columnCount[x]) * (double)System.Math.Max(1, rows);
            Put(result, o, sums[x * 3] / n);
            Put(result, o + 2, sums[x * 3 + 1] / n);
            Put(result, o + 4, sums[x * 3 + 2] / n);
            Put(result, o + 6, 1.0);
        }
        Array.Clear(sums);
    }

    private static void Put(byte[] target, long at, double value)
    {
        float v = (float)value;
        if (float.IsNaN(v))
            v = 0f;
        else if (float.IsInfinity(v) || v > 65504f)
            v = 65504f;
        ushort bits = BitConverter.HalfToUInt16Bits((Half)v);
        target[at] = (byte)bits;
        target[at + 1] = (byte)(bits >> 8);
    }

    // One scanline of RGBE into scan; returns where the next one starts, or -1.
    private static int ReadScanline(byte[] d, int at, int w, byte[] scan)
    {
        if (at + 4 > d.Length)
            return -1;
        bool rle = w >= 8 && w < 32768 && d[at] == 2 && d[at + 1] == 2 && (d[at + 2] & 0x80) == 0;
        if (!rle)
        {
            if (at + w * 4 > d.Length)
                return -1;
            if (d[at] == 1 && d[at + 1] == 1 && d[at + 2] == 1)
                return -1;
            Buffer.BlockCopy(d, at, scan, 0, w * 4);
            return at + w * 4;
        }
        if ((d[at + 2] << 8 | d[at + 3]) != w)
            return -1;
        at += 4;
        for (int c = 0; c < 4; c++)
        {
            int x = 0;
            while (x < w)
            {
                if (at >= d.Length)
                    return -1;
                int count = d[at++];
                if (count > 128)
                {
                    count -= 128;
                    if (count > w - x || at >= d.Length)
                        return -1;
                    byte v = d[at++];
                    for (int i = 0; i < count; i++)
                        scan[(x + i) * 4 + c] = v;
                }
                else
                {
                    if (count == 0 || count > w - x || at + count > d.Length)
                        return -1;
                    for (int i = 0; i < count; i++)
                        scan[(x + i) * 4 + c] = d[at + i];
                    at += count;
                }
                x += count;
            }
        }
        return at;
    }
}
