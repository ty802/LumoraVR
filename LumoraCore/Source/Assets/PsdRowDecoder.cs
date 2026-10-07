// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;

namespace Lumora.Core.Assets;

// The merged image of a PSD, decoded a row at a time into a BoxReducer. The channels are stored one after
// another, so each row reads its slice of each channel, raw or PackBits. 8-bit RGB and grey; a fourth
// channel is alpha and the white matte Photoshop composites into it is taken back out, the same as the
// full decoder. Null for anything else. -xlinka
public static class PsdRowDecoder
{
    public static byte[]? DecodeReduced(byte[] psd, int width, int height)
    {
        try
        {
            return Decode(psd, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? Decode(byte[] d, int targetWidth, int targetHeight)
    {
        if (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(4)) != 1)
            return null;
        int channels = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(12));
        int h = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(14));
        int w = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(18));
        int depth = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(22));
        int mode = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(24));
        if (depth != 8 || (mode != 3 && mode != 1) || w <= 0 || h <= 0 || channels < 1)
            return null;
        if (mode == 3 && channels < 3)
            return null;

        int at = 26;
        at += 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        at += 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        at += 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        int compression = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        at += 2;
        if (compression > 1)
            return null;

        int used = System.Math.Min(channels, mode == 3 ? 4 : 2);
        // Where each channel's row starts.
        var rowStart = new long[used * h];
        if (compression == 0)
        {
            for (int c = 0; c < used; c++)
                for (int y = 0; y < h; y++)
                    rowStart[c * h + y] = at + ((long)c * h + y) * w;
        }
        else
        {
            long data = at + (long)channels * h * 2;
            for (int c = 0; c < channels; c++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (c < used)
                        rowStart[c * h + y] = data;
                    data += BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at + (c * h + y) * 2));
                }
            }
        }

        var reducer = new BoxReducer(w, h, targetWidth, targetHeight);
        var plane = new byte[w];
        var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            for (int c = 0; c < 4; c++)
            {
                int source = mode == 3 ? c : (c < 3 ? 0 : 1);
                bool present = source < used;
                if (present)
                    ReadRow(d, rowStart[source * h + y], compression == 1, plane);
                for (int x = 0; x < w; x++)
                    row[x * 4 + c] = present ? plane[x] : (byte)255;
            }
            if (used == 4 || (mode == 1 && used == 2))
                RemoveMatte(row, w);
            reducer.AddRow(y, row);
        }
        return reducer.Finish();
    }

    private static void ReadRow(byte[] d, long at, bool packBits, byte[] plane)
    {
        if (!packBits)
        {
            Buffer.BlockCopy(d, (int)at, plane, 0, plane.Length);
            return;
        }
        int p = (int)at;
        int x = 0;
        while (x < plane.Length)
        {
            int n = (sbyte)d[p++];
            if (n >= 0)
            {
                int count = System.Math.Min(n + 1, plane.Length - x);
                Buffer.BlockCopy(d, p, plane, x, count);
                p += n + 1;
                x += count;
            }
            else if (n != -128)
            {
                byte v = d[p++];
                int count = System.Math.Min(1 - n, plane.Length - x);
                plane.AsSpan(x, count).Fill(v);
                x += count;
            }
        }
    }

    private static void RemoveMatte(byte[] row, int w)
    {
        for (int x = 0; x < w; x++)
        {
            int o = x * 4;
            byte a = row[o + 3];
            if (a == 0 || a == 255)
                continue;
            float ra = 1f / (a / 255f);
            float inverse = 255f * (1 - ra);
            row[o] = (byte)System.Math.Clamp(row[o] * ra + inverse, 0f, 255f);
            row[o + 1] = (byte)System.Math.Clamp(row[o + 1] * ra + inverse, 0f, 255f);
            row[o + 2] = (byte)System.Math.Clamp(row[o + 2] * ra + inverse, 0f, 255f);
        }
    }
}
