// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Lumora.Core.Assets;

// A BMP decoded a row at a time into a BoxReducer: 1, 4 and 8-bit palettes, 24-bit, and 32-bit plain or
// with bit masks, bottom-up or top-down. A 32-bit file whose alpha is zero everywhere is read as opaque,
// the same as the full decoder. Null for run-length and 16-bit files. -xlinka
public static class BmpRowDecoder
{
    public static byte[]? DecodeReduced(byte[] bmp, int width, int height)
    {
        try
        {
            return Decode(bmp, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? Decode(byte[] d, int targetWidth, int targetHeight)
    {
        int dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(10));
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(14));
        if (headerSize < 40)
            return null;
        int w = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(18));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(22));
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(28));
        int compression = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(30));
        int colorsUsed = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(46));
        bool topDown = rawHeight < 0;
        int h = System.Math.Abs(rawHeight);
        if (w <= 0 || h <= 0)
            return null;
        if (compression is not (0 or 3 or 6) || bpp is not (1 or 4 or 8 or 24 or 32))
            return null;

        uint rMask = 0x00FF0000, gMask = 0x0000FF00, bMask = 0x000000FF, aMask = 0xFF000000;
        if (bpp == 32 && compression is 3 or 6)
        {
            int masks = headerSize >= 52 || compression == 6 ? 14 + 40 : 14 + headerSize;
            rMask = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(masks));
            gMask = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(masks + 4));
            bMask = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(masks + 8));
            aMask = headerSize >= 56 || compression == 6 ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(masks + 12)) : 0;
        }
        else if (compression != 0)
            return null;

        byte[]? palette = null;
        if (bpp <= 8)
        {
            int entries = colorsUsed > 0 ? colorsUsed : 1 << bpp;
            palette = new byte[entries * 4];
            int p = 14 + headerSize;
            for (int i = 0; i < entries && p + 3 < d.Length; i++, p += 4)
            {
                palette[i * 4] = d[p + 2];
                palette[i * 4 + 1] = d[p + 1];
                palette[i * 4 + 2] = d[p];
                palette[i * 4 + 3] = 255;
            }
        }

        int stride = ((bpp * w + 31) / 32) * 4;
        if ((long)dataOffset + (long)stride * h > d.Length)
            return null;

        // Opaque when the alpha mask is present but every value under it is zero.
        bool useAlpha = false;
        if (bpp == 32 && aMask != 0)
        {
            for (int y = 0; y < h && !useAlpha; y++)
            {
                int rowStart = dataOffset + y * stride;
                for (int x = 0; x < w; x++)
                {
                    if ((BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(rowStart + x * 4)) & aMask) != 0)
                    {
                        useAlpha = true;
                        break;
                    }
                }
            }
        }

        var reducer = new BoxReducer(w, h, targetWidth, targetHeight);
        var row = new byte[w * 4];
        for (int r = 0; r < h; r++)
        {
            int start = dataOffset + r * stride;
            for (int x = 0; x < w; x++)
            {
                int o = x * 4;
                switch (bpp)
                {
                    case 24:
                        row[o] = d[start + x * 3 + 2];
                        row[o + 1] = d[start + x * 3 + 1];
                        row[o + 2] = d[start + x * 3];
                        row[o + 3] = 255;
                        break;
                    case 32:
                        {
                            uint v = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(start + x * 4));
                            row[o] = Channel(v, rMask);
                            row[o + 1] = Channel(v, gMask);
                            row[o + 2] = Channel(v, bMask);
                            row[o + 3] = useAlpha ? Channel(v, aMask) : (byte)255;
                            break;
                        }
                    default:
                        {
                            int bit = x * bpp;
                            int index = (d[start + (bit >> 3)] >> (8 - bpp - (bit & 7))) & ((1 << bpp) - 1);
                            int p = index * 4;
                            if (palette != null && p + 3 < palette.Length)
                            {
                                row[o] = palette[p];
                                row[o + 1] = palette[p + 1];
                                row[o + 2] = palette[p + 2];
                            }
                            else
                                row[o] = row[o + 1] = row[o + 2] = 0;
                            row[o + 3] = 255;
                            break;
                        }
                }
            }
            reducer.AddRow(topDown ? r : h - 1 - r, row);
        }
        return reducer.Finish();
    }

    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0)
            return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        uint bits = mask >> shift;
        uint v = (value & mask) >> shift;
        int width = BitOperations.PopCount(bits);
        return width >= 8 ? (byte)(v >> (width - 8)) : (byte)(v * 255 / bits);
    }
}
