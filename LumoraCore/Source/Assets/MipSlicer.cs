// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;

namespace Lumora.Core.Assets;

// A DDS or KTX that carries its own mip chain, cut down to start at the first level within the size cap,
// so the levels above it are never decoded at all. Single 2D images only: cube maps, arrays and volumes,
// and a file whose chain never gets under the cap, are left for the caller. -xlinka
public static class MipSlicer
{
    public static bool TrySlice(byte[] data, int cap, out byte[] sliced)
    {
        sliced = Array.Empty<byte>();
        try
        {
            return ImageHeader.Identify(data) switch
            {
                ImageContainer.Dds => SliceDds(data, cap, out sliced),
                ImageContainer.Ktx => SliceKtx(data, cap, out sliced),
                _ => false,
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Level(int size, int level) => System.Math.Max(1, size >> level);

    private static bool SliceDds(byte[] d, int cap, out byte[] sliced)
    {
        sliced = Array.Empty<byte>();
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12));
        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(16));
        int depth = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(24));
        int mips = System.Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(28)));
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(80));
        var fourCC = d.AsSpan(84, 4);
        int rgbBits = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(88));
        uint caps2 = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(112));
        if (depth > 1 || (caps2 & 0x200) != 0)
            return false;

        int headerBytes = 128;
        int blockBytes = 0, pixelBits = 0;
        if ((pfFlags & 0x4) != 0)
        {
            if (fourCC.SequenceEqual("DXT1"u8) || fourCC.SequenceEqual("ATI1"u8) || fourCC.SequenceEqual("BC4U"u8) || fourCC.SequenceEqual("BC4S"u8))
                blockBytes = 8;
            else if (fourCC.SequenceEqual("DXT2"u8) || fourCC.SequenceEqual("DXT3"u8) || fourCC.SequenceEqual("DXT4"u8) || fourCC.SequenceEqual("DXT5"u8)
                     || fourCC.SequenceEqual("ATI2"u8) || fourCC.SequenceEqual("BC5U"u8) || fourCC.SequenceEqual("BC5S"u8))
                blockBytes = 16;
            else if (fourCC.SequenceEqual("DX10"u8))
            {
                headerBytes += 20;
                int format = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(128));
                int dimension = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(132));
                int arraySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(140));
                if (dimension != 3 || arraySize > 1)
                    return false;
                blockBytes = format switch
                {
                    70 or 71 or 72 or 79 or 80 or 81 => 8,
                    73 or 74 or 75 or 76 or 77 or 78 or 82 or 83 or 84 or 94 or 95 or 96 or 97 or 98 or 99 => 16,
                    _ => 0,
                };
                pixelBits = format switch
                {
                    27 or 28 or 29 or 87 or 88 or 91 or 93 => 32,
                    10 or 11 => 64,
                    2 => 128,
                    _ => 0,
                };
                if (blockBytes == 0 && pixelBits == 0)
                    return false;
            }
            else
                return false;
        }
        else
            pixelBits = rgbBits;
        if (blockBytes == 0 && (pixelBits <= 0 || pixelBits % 8 != 0))
            return false;

        long LevelBytes(int level)
        {
            int w = Level(width, level), h = Level(height, level);
            return blockBytes > 0
                ? (long)((w + 3) / 4) * ((h + 3) / 4) * blockBytes
                : (long)w * h * (pixelBits / 8);
        }

        int first = 0;
        while (first < mips && System.Math.Max(Level(width, first), Level(height, first)) > cap)
            first++;
        if (first == 0 || first >= mips)
            return false;

        long skip = 0;
        for (int level = 0; level < first; level++)
            skip += LevelBytes(level);
        long keep = d.LongLength - headerBytes - skip;
        if (keep <= 0)
            return false;

        sliced = new byte[headerBytes + keep];
        Buffer.BlockCopy(d, 0, sliced, 0, headerBytes);
        Buffer.BlockCopy(d, (int)(headerBytes + skip), sliced, headerBytes, (int)keep);
        int newWidth = Level(width, first), newHeight = Level(height, first);
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(12), (uint)newHeight);
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(16), (uint)newWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(28), (uint)(mips - first));
        long pitch = blockBytes > 0 ? LevelBytes(first) : (long)newWidth * (pixelBits / 8);
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(20), (uint)pitch);
        return true;
    }

    // KTX 1: a 64-byte header, key-value data, then each level as a size and its bytes padded to four.
    private static bool SliceKtx(byte[] d, int cap, out byte[] sliced)
    {
        sliced = Array.Empty<byte>();
        if (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12)) != 0x04030201)
            return false;
        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(36));
        int height = System.Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(40)));
        int depth = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(44));
        int arrays = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(48));
        int faces = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(52));
        int mips = System.Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(56)));
        int keyValueBytes = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(60));
        if (depth > 1 || arrays > 0 || faces != 1)
            return false;

        int first = 0;
        while (first < mips && System.Math.Max(Level(width, first), Level(height, first)) > cap)
            first++;
        if (first == 0 || first >= mips)
            return false;

        int at = 64 + keyValueBytes;
        for (int level = 0; level < first; level++)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
            at += 4 + ((size + 3) & ~3);
            if (at > d.Length)
                return false;
        }
        int head = 64 + keyValueBytes;
        sliced = new byte[head + d.Length - at];
        Buffer.BlockCopy(d, 0, sliced, 0, head);
        Buffer.BlockCopy(d, at, sliced, head, d.Length - at);
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(36), (uint)Level(width, first));
        if (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(40)) != 0)
            BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(40), (uint)Level(height, first));
        BinaryPrimitives.WriteUInt32LittleEndian(sliced.AsSpan(56), (uint)(mips - first));
        return true;
    }
}
