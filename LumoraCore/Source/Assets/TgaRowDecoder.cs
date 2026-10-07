// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;

namespace Lumora.Core.Assets;

// A TGA decoded a row at a time into a BoxReducer: true colour, grey and colour-mapped, raw or
// run-length, either origin. Null for a right-to-left image or anything malformed. -xlinka
public static class TgaRowDecoder
{
    public static byte[]? DecodeReduced(byte[] tga, int width, int height)
    {
        try
        {
            return Decode(tga, width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? Decode(byte[] d, int targetWidth, int targetHeight)
    {
        int idLength = d[0], mapType = d[1], type = d[2];
        int mapStart = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(3));
        int mapLength = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(5));
        int mapDepth = d[7];
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(12));
        int h = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(14));
        int depth = d[16];
        int descriptor = d[17];
        if (w <= 0 || h <= 0 || (descriptor & 0x10) != 0)
            return null;
        bool rle = type >= 9;
        int baseType = rle ? type - 8 : type;
        bool topDown = (descriptor & 0x20) != 0;

        int at = 18 + idLength;
        byte[]? map = null;
        if (mapType == 1)
        {
            int entryBytes = (mapDepth + 7) / 8;
            map = new byte[(mapStart + mapLength) * 4];
            for (int i = 0; i < mapLength; i++)
                ReadPixel(d, at + i * entryBytes, mapDepth, map, (mapStart + i) * 4);
            at += mapLength * entryBytes;
        }
        if (baseType == 1 && map == null)
            return null;
        if (baseType is not (1 or 2 or 3))
            return null;

        int pixelBytes = (depth + 7) / 8;
        var reducer = new BoxReducer(w, h, targetWidth, targetHeight);
        var row = new byte[w * 4];
        var pixel = new byte[4];
        int runLeft = 0;
        bool runRepeat = false;

        void Next(byte[] target, int offset)
        {
            if (rle)
            {
                if (runLeft == 0)
                {
                    int header = d[at++];
                    runLeft = (header & 0x7F) + 1;
                    runRepeat = (header & 0x80) != 0;
                    if (runRepeat)
                    {
                        Convert(d, at, baseType, depth, map, pixel, 0);
                        at += pixelBytes;
                    }
                }
                runLeft--;
                if (runRepeat)
                {
                    target[offset] = pixel[0];
                    target[offset + 1] = pixel[1];
                    target[offset + 2] = pixel[2];
                    target[offset + 3] = pixel[3];
                    return;
                }
            }
            Convert(d, at, baseType, depth, map, target, offset);
            at += pixelBytes;
        }

        for (int r = 0; r < h; r++)
        {
            for (int x = 0; x < w; x++)
                Next(row, x * 4);
            reducer.AddRow(topDown ? r : h - 1 - r, row);
        }
        return reducer.Finish();
    }

    private static void Convert(byte[] d, int at, int baseType, int depth, byte[]? map, byte[] target, int offset)
    {
        if (baseType == 1)
        {
            int index = depth == 16 ? d[at] | d[at + 1] << 8 : d[at];
            int m = index * 4;
            if (map == null || m + 3 >= map.Length)
            {
                target[offset] = target[offset + 1] = target[offset + 2] = 0;
                target[offset + 3] = 255;
                return;
            }
            target[offset] = map[m];
            target[offset + 1] = map[m + 1];
            target[offset + 2] = map[m + 2];
            target[offset + 3] = map[m + 3];
            return;
        }
        if (baseType == 3)
        {
            byte v = d[at];
            target[offset] = target[offset + 1] = target[offset + 2] = v;
            target[offset + 3] = depth == 16 ? d[at + 1] : (byte)255;
            return;
        }
        ReadPixel(d, at, depth, target, offset);
    }

    // BGR(A) or 5-5-5 colour to RGBA8.
    private static void ReadPixel(byte[] d, int at, int depth, byte[] target, int offset)
    {
        switch (depth)
        {
            case 15:
            case 16:
                {
                    int v = d[at] | d[at + 1] << 8;
                    target[offset] = (byte)(((v >> 10) & 31) * 255 / 31);
                    target[offset + 1] = (byte)(((v >> 5) & 31) * 255 / 31);
                    target[offset + 2] = (byte)((v & 31) * 255 / 31);
                    target[offset + 3] = 255;
                    break;
                }
            case 24:
                target[offset] = d[at + 2];
                target[offset + 1] = d[at + 1];
                target[offset + 2] = d[at];
                target[offset + 3] = 255;
                break;
            case 32:
                target[offset] = d[at + 2];
                target[offset + 1] = d[at + 1];
                target[offset + 2] = d[at];
                target[offset + 3] = d[at + 3];
                break;
            default:
                target[offset] = target[offset + 1] = target[offset + 2] = d[at];
                target[offset + 3] = 255;
                break;
        }
    }
}
