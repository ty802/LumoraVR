// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Lumora.Core.Assets;

public enum ImageContainer
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    Gif,
    Bmp,
    Tga,
    Dds,
    Ktx,
    Ktx2,
    Radiance,
    Exr,
    Tiff,
    Psd,
}

// What an encoded picture is and how big, read off its header without decoding a pixel. The texture size
// cap is decided from this before anything is allocated for the picture itself. -xlinka
public static class ImageHeader
{
    public static bool TryProbe(ReadOnlySpan<byte> data, out ImageContainer container, out int width, out int height)
    {
        container = ImageContainer.Unknown;
        width = 0;
        height = 0;
        try
        {
            container = Identify(data);
            bool ok = container switch
            {
                ImageContainer.Png => Png(data, out width, out height),
                ImageContainer.Jpeg => Jpeg(data, out width, out height),
                ImageContainer.WebP => WebP(data, out width, out height),
                ImageContainer.Gif => Gif(data, out width, out height),
                ImageContainer.Bmp => Bmp(data, out width, out height),
                ImageContainer.Tga => Tga(data, out width, out height),
                ImageContainer.Dds => Dds(data, out width, out height),
                ImageContainer.Ktx => Ktx(data, out width, out height),
                ImageContainer.Ktx2 => Ktx2(data, out width, out height),
                ImageContainer.Radiance => Radiance(data, out width, out height),
                ImageContainer.Exr => Exr(data, out width, out height),
                ImageContainer.Tiff => Tiff(data, out width, out height),
                ImageContainer.Psd => Psd(data, out width, out height),
                _ => false,
            };
            if (!ok || width <= 0 || height <= 0)
            {
                width = 0;
                height = 0;
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    public static ImageContainer Identify(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            return ImageContainer.Png;
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return ImageContainer.Jpeg;
        if (d.Length >= 12 && d.StartsWith("RIFF"u8) && d.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ImageContainer.WebP;
        if (d.Length >= 6 && (d.StartsWith("GIF87a"u8) || d.StartsWith("GIF89a"u8)))
            return ImageContainer.Gif;
        if (d.Length >= 26 && d[0] == 'B' && d[1] == 'M')
            return ImageContainer.Bmp;
        if (d.Length >= 128 && d.StartsWith("DDS "u8))
            return ImageContainer.Dds;
        if (d.Length >= 64 && d[0] == 0xAB && d[1] == 0x4B && d[2] == 0x54 && d[3] == 0x58 && d[4] == 0x20 && d[5] == 0x31)
            return ImageContainer.Ktx;
        if (d.Length >= 48 && d[0] == 0xAB && d[1] == 0x4B && d[2] == 0x54 && d[3] == 0x58 && d[4] == 0x20 && d[5] == 0x32)
            return ImageContainer.Ktx2;
        if (d.Length >= 2 && d[0] == '#' && d[1] == '?')
            return ImageContainer.Radiance;
        if (d.Length >= 4 && d[0] == 0x76 && d[1] == 0x2F && d[2] == 0x31 && d[3] == 0x01)
            return ImageContainer.Exr;
        if (d.Length >= 8 && ((d[0] == 'I' && d[1] == 'I' && d[2] == 42 && d[3] == 0) || (d[0] == 'M' && d[1] == 'M' && d[2] == 0 && d[3] == 42)))
            return ImageContainer.Tiff;
        if (d.Length >= 26 && d.StartsWith("8BPS"u8))
            return ImageContainer.Psd;
        if (LooksLikeTga(d))
            return ImageContainer.Tga;
        return ImageContainer.Unknown;
    }

    // TGA has no magic: the image type, colour map type and pixel depth have to make sense together.
    public static bool LooksLikeTga(ReadOnlySpan<byte> d)
    {
        if (d.Length < 18)
            return false;
        byte mapType = d[1], type = d[2], depth = d[16];
        if (mapType > 1)
            return false;
        if (type is not (1 or 2 or 3 or 9 or 10 or 11))
            return false;
        if ((type is 1 or 9) != (mapType == 1))
            return false;
        if (depth is not (8 or 15 or 16 or 24 or 32))
            return false;
        return BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(12)) > 0 && BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(14)) > 0;
    }

    private static bool Png(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16));
        h = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(20));
        return d.Slice(12, 4).SequenceEqual("IHDR"u8);
    }

    // The first start-of-frame marker carries the size. DHT, JPG and DAC share the C4/C8/CC range.
    private static bool Jpeg(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        int i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF)
            {
                i++;
                continue;
            }
            byte marker = d[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2));
            if (marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > d.Length)
                    return false;
                h = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5));
                w = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7));
                return true;
            }
            if (marker == 0xDA || marker == 0xD9)
                return false;
            i += 2 + length;
        }
        return false;
    }

    private static bool WebP(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        var chunk = d.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8) && d.Length >= 30)
        {
            w = 1 + (d[24] | d[25] << 8 | d[26] << 16);
            h = 1 + (d[27] | d[28] << 8 | d[29] << 16);
            return true;
        }
        if (chunk.SequenceEqual("VP8L"u8) && d.Length >= 25 && d[20] == 0x2F)
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(21));
            w = (int)(bits & 0x3FFF) + 1;
            h = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }
        if (chunk.SequenceEqual("VP8 "u8) && d.Length >= 30)
        {
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A)
                return false;
            w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(26)) & 0x3FFF;
            h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(28)) & 0x3FFF;
            return true;
        }
        return false;
    }

    private static bool Gif(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(6));
        h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(8));
        return d.Length >= 10;
    }

    private static bool Bmp(ReadOnlySpan<byte> d, out int w, out int h)
    {
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(14));
        if (headerSize == 12)
        {
            w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(18));
            h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(20));
            return true;
        }
        w = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(18));
        h = System.Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d.Slice(22)));
        return true;
    }

    private static bool Tga(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(12));
        h = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(14));
        return true;
    }

    private static bool Dds(ReadOnlySpan<byte> d, out int w, out int h)
    {
        h = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(12));
        w = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(16));
        return true;
    }

    private static bool Ktx(ReadOnlySpan<byte> d, out int w, out int h)
    {
        bool little = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(12)) == 0x04030201;
        w = (int)(little ? BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(36)) : BinaryPrimitives.ReadUInt32BigEndian(d.Slice(36)));
        h = (int)(little ? BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(40)) : BinaryPrimitives.ReadUInt32BigEndian(d.Slice(40)));
        if (h == 0)
            h = 1;
        return true;
    }

    private static bool Ktx2(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(20));
        h = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(24));
        if (h == 0)
            h = 1;
        return true;
    }

    // Text header lines, a blank line, then the resolution line: "-Y 512 +X 1024" and its variants.
    private static bool Radiance(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        int end = System.Math.Min(d.Length, 8192);
        int line = 0;
        bool blank = false;
        for (int i = 0; i < end; i++)
        {
            if (d[i] != '\n')
                continue;
            if (blank)
                return ParseRadianceSize(Encoding.ASCII.GetString(d.Slice(line, i - line)), out w, out h);
            blank = i == line;
            line = i + 1;
        }
        return false;
    }

    public static bool ParseRadianceSize(string text, out int w, out int h)
    {
        w = h = 0;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;
        for (int i = 0; i < 4; i += 2)
        {
            if (!int.TryParse(parts[i + 1], out int n))
                return false;
            if (parts[i].EndsWith('Y'))
                h = n;
            else if (parts[i].EndsWith('X'))
                w = n;
        }
        return w > 0 && h > 0;
    }

    // Attributes are name, type, size, value until an empty name; the data window gives the size.
    private static bool Exr(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        int i = 8;
        while (i < d.Length && d[i] != 0)
        {
            int nameEnd = d.Slice(i).IndexOf((byte)0);
            if (nameEnd < 0)
                return false;
            var name = d.Slice(i, nameEnd);
            i += nameEnd + 1;
            int typeEnd = d.Slice(i).IndexOf((byte)0);
            if (typeEnd < 0)
                return false;
            i += typeEnd + 1;
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i));
            i += 4;
            if (name.SequenceEqual("dataWindow"u8) && size >= 16)
            {
                int xMin = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i));
                int yMin = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i + 4));
                int xMax = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i + 8));
                int yMax = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i + 12));
                w = xMax - xMin + 1;
                h = yMax - yMin + 1;
                return true;
            }
            i += size;
        }
        return false;
    }

    private static bool Tiff(ReadOnlySpan<byte> d, out int w, out int h)
    {
        w = h = 0;
        bool little = d[0] == 'I';
        int ifd = (int)ReadU32(d, 4, little);
        if (ifd <= 0 || ifd + 2 > d.Length)
            return false;
        int count = ReadU16(d, ifd, little);
        for (int n = 0; n < count; n++)
        {
            int entry = ifd + 2 + n * 12;
            if (entry + 12 > d.Length)
                break;
            ushort tag = ReadU16(d, entry, little);
            ushort type = ReadU16(d, entry + 2, little);
            int value = type == 3 ? ReadU16(d, entry + 8, little) : (int)ReadU32(d, entry + 8, little);
            if (tag == 256)
                w = value;
            else if (tag == 257)
                h = value;
        }
        return w > 0 && h > 0;
    }

    private static uint ReadU32(ReadOnlySpan<byte> d, int at, bool little)
        => little ? BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.Slice(at));

    private static ushort ReadU16(ReadOnlySpan<byte> d, int at, bool little)
        => little ? BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.Slice(at));

    private static bool Psd(ReadOnlySpan<byte> d, out int w, out int h)
    {
        h = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(14));
        w = (int)BinaryPrimitives.ReadUInt32BigEndian(d.Slice(18));
        return true;
    }
}
