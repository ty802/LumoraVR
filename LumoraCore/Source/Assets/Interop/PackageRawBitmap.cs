// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lumora.Core.Math;
using Lumora.Core.Photos;

namespace Lumora.Core.Assets.Interop;

// The package's raw bitmap format: pixels as the GPU wants them, not an image file. Baked reflection
// probes and generated skies arrive this way. Every variant opens with a length-prefixed magic string
// and an int32 version:
//
//   "Bmp2X"    int32 width, int32 height, uint16 format, byte flags (bit 0 mips, bit 1 rows bottom-up),
//              byte encoding, then from version 1 the colour profile as a string
//   "BmpCube"  int32 width, int32 height, the format (v0 a varint legacy ordinal, v1 its name, v2+ uint16),
//              v0-1 a mips bool, v2+ flags byte and encoding byte, v3+ the profile string
//   "Bmp3D"    int32 version, int32 width, height and depth, the format's name (v0 a legacy ordinal, not
//              read), a mips bool, from version 2 the profile's name, then the pixels, always PackageLzma
//
// Encoding 0 is the pixels verbatim, 1 is PackageLzma. Each layer (a cube has six, in +X -X +Y -Y +Z -Z
// order) is its whole mip chain back to back, mip 0 first, rows top-down for cubes. A cube face uses the
// usual graphics-API layout in package space, which is the table CubemapProjector.FaceDirection states.
//
// A cube becomes an equirectangular panorama, because that is what StaticCubemap loads: Radiance when
// its pixels are half or float, so a lamp stored at 50 stays 50 (baked probe cubes are all HDR), and a
// PNG otherwise, a Linear one encoded to sRGB on the way since 8-bit faces are sampled as sRGB. A flat bitmap
// becomes a PNG, or Radiance when its pixels are half or float, which the texture path reads as HDR.
// The texture path's Radiance reader only gets run-length scanlines right, and those need a width of
// at least 8, so an HDR strip narrower than that goes out as a clamped PNG instead. -xlinka
public static class PackageRawBitmap
{
    public enum Shape
    {
        Flat,
        Cube,
        Volume,
    }

    // Ordinals are what the package writes on disk.
    public enum PixelFormat : ushort
    {
        Unknown = 0,
        Alpha8 = 1,
        R8 = 2,
        RGB24 = 16,
        ARGB32 = 17,
        RGBA32 = 18,
        BGRA32 = 19,
        RGB565 = 24,
        BGR565 = 25,
        RGBAHalf = 32,
        ARGBHalf = 33,
        RHalf = 34,
        RGHalf = 35,
        RGBAFloat = 48,
        ARGBFloat = 49,
        RFloat = 50,
        RGFloat = 51,
        BC1 = 64,
        BC2 = 65,
        BC3 = 66,
        BC4 = 67,
        BC5 = 68,
        BC6H = 69,
        BC7 = 70,
        ETC2_RGB = 96,
        ETC2_RGBA1 = 97,
        ETC2_RGBA8 = 98,
        ASTC_4x4 = 128,
        ASTC_5x5 = 129,
        ASTC_6x6 = 130,
        ASTC_8x8 = 131,
        ASTC_10x10 = 132,
        ASTC_12x12 = 133,
    }

    public readonly record struct Header(
        Shape Shape,
        int Version,
        int Width,
        int Height,
        PixelFormat Format,
        bool Mipmaps,
        bool BottomUp,
        bool Lzma,
        ColorProfile Profile,
        int DataOffset)
    {
        public bool IsHdr => PackageRawBitmap.IsHdr(Format);
    }

    private const string FlatMagic = "Bmp2X";
    private const string CubeMagic = "BmpCube";
    private const string VolumeMagic = "Bmp3D";

    private const int MaxDimension = 16384;

    // Panorama face cap: 8192x4096 out. Past it the projection downsamples rather than allocating gigabytes.
    private const int MaxPanoramaFace = 2048;

    private const int RadianceMinWidth = 8;

    public static bool IsRawBitmap(ReadOnlySpan<byte> payload)
    {
        int offset = 0;
        if (!TryReadString(payload, ref offset, 16, out var magic))
            return false;
        return magic == FlatMagic || magic == CubeMagic || magic == VolumeMagic;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> payload, out Header header, out string problem)
    {
        header = default;
        int offset = 0;
        if (!TryReadString(payload, ref offset, 16, out var magic))
        {
            problem = "not a raw bitmap";
            return false;
        }

        Shape shape = magic switch
        {
            FlatMagic => Shape.Flat,
            CubeMagic => Shape.Cube,
            VolumeMagic => Shape.Volume,
            _ => (Shape)(-1),
        };
        if ((int)shape < 0)
        {
            problem = "not a raw bitmap";
            return false;
        }
        if (shape == Shape.Volume)
        {
            problem = "volume bitmaps are not supported";
            return false;
        }

        if (!TryReadInt32(payload, ref offset, out int version) ||
            !TryReadInt32(payload, ref offset, out int width) ||
            !TryReadInt32(payload, ref offset, out int height))
        {
            problem = "header is truncated";
            return false;
        }

        int maxVersion = shape == Shape.Cube ? 3 : 1;
        if (version < 0 || version > maxVersion)
        {
            problem = $"{magic} version {version} is newer than this reader";
            return false;
        }
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension)
        {
            problem = $"implausible size {width}x{height}";
            return false;
        }

        PixelFormat format;
        bool mipmaps;
        bool bottomUp = false;
        int encoding;
        if (shape == Shape.Flat)
        {
            if (!TryReadUInt16(payload, ref offset, out ushort ordinal) ||
                !TryReadByte(payload, ref offset, out byte flags) ||
                !TryReadByte(payload, ref offset, out byte encodingByte))
            {
                problem = "header is truncated";
                return false;
            }
            format = (PixelFormat)ordinal;
            mipmaps = (flags & 1) != 0;
            bottomUp = (flags & 2) != 0;
            encoding = encodingByte;
        }
        else
        {
            if (!TryReadCubeFormat(payload, ref offset, version, out format))
            {
                problem = "unreadable pixel format";
                return false;
            }
            if (version < 2)
            {
                if (!TryReadByte(payload, ref offset, out byte mipsByte))
                {
                    problem = "header is truncated";
                    return false;
                }
                mipmaps = mipsByte != 0;
                encoding = 1;
            }
            else
            {
                if (!TryReadByte(payload, ref offset, out byte flags) ||
                    !TryReadByte(payload, ref offset, out byte encodingByte))
                {
                    problem = "header is truncated";
                    return false;
                }
                mipmaps = (flags & 1) != 0;
                encoding = encodingByte;
            }
        }

        var profile = ColorProfile.sRGB;
        bool hasProfile = shape == Shape.Flat ? version > 0 : version > 2;
        if (hasProfile)
        {
            if (!TryReadString(payload, ref offset, 32, out var profileName))
            {
                problem = "header is truncated";
                return false;
            }
            switch (profileName)
            {
                case "Linear":
                    profile = ColorProfile.Linear;
                    break;
                case "sRGB":
                case "sRGBAlpha":
                    profile = ColorProfile.sRGB;
                    break;
                default:
                    problem = $"unknown colour profile '{profileName}'";
                    return false;
            }
        }

        if (encoding != 0 && encoding != 1)
        {
            problem = $"unknown pixel encoding {encoding}";
            return false;
        }
        if (shape == Shape.Cube && width != height)
        {
            problem = $"cube faces are {width}x{height}, not square";
            return false;
        }

        header = new Header(shape, version, width, height, format, mipmaps, bottomUp, encoding == 1, profile, offset);
        problem = string.Empty;
        return true;
    }

    public static bool TryConvert(byte[] payload, out byte[] file, out string extension, out string problem)
    {
        file = Array.Empty<byte>();
        extension = string.Empty;
        if (payload == null)
        {
            problem = "no payload";
            return false;
        }
        if (IsVolume(payload))
            return TryConvertVolume(payload, out file, out extension, out problem);
        if (!TryReadHeader(payload, out var header, out problem))
            return false;

        int bytesPerPixel = BytesPerPixel(header.Format);
        if (bytesPerPixel == 0)
        {
            problem = IsBlockCompressed(header.Format)
                ? $"{header.Format} is block-compressed; only uncompressed pixels are decoded"
                : $"unknown pixel format {(int)header.Format}";
            return false;
        }

        try
        {
            if (!TryReadPixels(payload, header, bytesPerPixel, out var pixels, out int start, out problem))
                return false;

            if (header.Shape == Shape.Cube)
            {
                file = CubeToPanorama(pixels, start, header, bytesPerPixel, header.IsHdr);
                extension = header.IsHdr ? ".hdr" : ".png";
            }
            else if (header.IsHdr && header.Width >= RadianceMinWidth)
            {
                file = FlatToRadiance(pixels, start, header, bytesPerPixel);
                extension = ".hdr";
            }
            else
            {
                file = FlatToPng(pixels, start, header, bytesPerPixel);
                extension = ".png";
            }
        }
        catch (InvalidDataException ex)
        {
            problem = ex.Message;
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool IsVolume(ReadOnlySpan<byte> payload)
    {
        int offset = 0;
        return TryReadString(payload, ref offset, 16, out var magic) && magic == VolumeMagic;
    }

    // A volume becomes a strip of its depth slices side by side, slice z at columns z * width onwards, rows as
    // stored: the layout LUTMaterial reads (red across a slice, green down it, blue picking the slice), and a
    // lookup table is what volumes in a package are. Mip 0 only; values as stored, like a flat bitmap. -xlinka
    private static bool TryConvertVolume(byte[] payload, out byte[] file, out string extension, out string problem)
    {
        file = Array.Empty<byte>();
        extension = string.Empty;
        int offset = 0;
        if (!TryReadString(payload, ref offset, 16, out _)
            || !TryReadInt32(payload, ref offset, out int version)
            || !TryReadInt32(payload, ref offset, out int width)
            || !TryReadInt32(payload, ref offset, out int height)
            || !TryReadInt32(payload, ref offset, out int depth))
        {
            problem = "volume header is truncated";
            return false;
        }
        if (version < 1 || version > 2)
        {
            problem = $"Bmp3D version {version} is not read";
            return false;
        }
        if (width < 1 || height < 1 || depth < 1 || (long)width * depth > MaxDimension || height > MaxDimension)
        {
            problem = $"implausible volume {width}x{height}x{depth}";
            return false;
        }
        if (!TryReadString(payload, ref offset, 32, out var formatName) || !Enum.TryParse(formatName, out PixelFormat format)
            || !TryReadByte(payload, ref offset, out byte mipsByte))
        {
            problem = "volume header is truncated";
            return false;
        }
        if (version > 1 && !TryReadString(payload, ref offset, 32, out _))
        {
            problem = "volume header is truncated";
            return false;
        }
        int bytesPerPixel = BytesPerPixel(format);
        if (bytesPerPixel == 0)
        {
            problem = $"{format} volumes are not decoded";
            return false;
        }

        long levelPixels = (long)width * height * depth;
        long expected = levelPixels;
        if (mipsByte != 0)
        {
            for (int w = width, h = height, d = depth; w > 1 || h > 1 || d > 1;)
            {
                w = System.Math.Max(w / 2, 1);
                h = System.Math.Max(h / 2, 1);
                d = System.Math.Max(d / 2, 1);
                expected += (long)w * h * d;
            }
        }
        expected *= bytesPerPixel;
        var body = payload.AsSpan(offset);
        if (expected > Array.MaxLength || !PackageLzma.TryReadHeader(body, out long decoded, out _) || decoded < levelPixels * bytesPerPixel)
        {
            problem = "volume pixels do not match its size and format";
            return false;
        }

        try
        {
            var pixels = PackageLzma.Decode(body);
            int stripWidth = width * depth;
            var rgba = new byte[(long)stripWidth * height * 4];
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    long source = ((long)z * height + y) * width * bytesPerPixel;
                    int row = (y * stripWidth + z * width) * 4;
                    for (int x = 0; x < width; x++)
                        WriteByteColor(rgba, row + x * 4, ReadTexel(pixels, source + (long)x * bytesPerPixel, format));
                }
            }
            file = PhotoEncoder.Png(rgba, stripWidth, height);
            extension = ".png";
        }
        catch (InvalidDataException ex)
        {
            problem = ex.Message;
            return false;
        }
        problem = string.Empty;
        return true;
    }

    public static bool IsHdr(PixelFormat format) => format switch
    {
        PixelFormat.RGBAHalf or PixelFormat.ARGBHalf or PixelFormat.RHalf or PixelFormat.RGHalf => true,
        PixelFormat.RGBAFloat or PixelFormat.ARGBFloat or PixelFormat.RFloat or PixelFormat.RGFloat => true,
        _ => false,
    };

    public static bool IsBlockCompressed(PixelFormat format)
    {
        int value = (int)format;
        return value is >= 64 and <= 70 or >= 96 and <= 98 or >= 128 and <= 133;
    }

    // 0 for anything not decoded here.
    public static int BytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Alpha8 or PixelFormat.R8 => 1,
        PixelFormat.RGB565 or PixelFormat.BGR565 or PixelFormat.RHalf => 2,
        PixelFormat.RGB24 => 3,
        PixelFormat.ARGB32 or PixelFormat.RGBA32 or PixelFormat.BGRA32 => 4,
        PixelFormat.RGHalf or PixelFormat.RFloat => 4,
        PixelFormat.RGBAHalf or PixelFormat.ARGBHalf or PixelFormat.RGFloat => 8,
        PixelFormat.RGBAFloat or PixelFormat.ARGBFloat => 16,
        _ => 0,
    };

    // Pixels in one layer: mip 0 alone, or the whole chain down to 1x1 when the bitmap has mips.
    public static long LayerPixels(int width, int height, bool mipmaps)
    {
        if (!mipmaps)
            return (long)width * height;
        long total = 0;
        while (width > 1 || height > 1)
        {
            total += (long)width * height;
            width = System.Math.Max(width / 2, 1);
            height = System.Math.Max(height / 2, 1);
        }
        return total + 1;
    }

    private static bool TryReadPixels(byte[] payload, in Header header, int bytesPerPixel,
        out byte[] pixels, out int start, out string problem)
    {
        int layers = header.Shape == Shape.Cube ? 6 : 1;
        long expected = LayerPixels(header.Width, header.Height, header.Mipmaps) * layers * bytesPerPixel;
        if (expected > Array.MaxLength)
        {
            pixels = Array.Empty<byte>();
            start = 0;
            problem = $"{expected} bytes of pixels is too large";
            return false;
        }

        var body = payload.AsSpan(header.DataOffset);
        if (!header.Lzma)
        {
            if (body.Length < expected)
            {
                pixels = Array.Empty<byte>();
                start = 0;
                problem = $"holds {body.Length} bytes of pixels, its size and format need {expected}";
                return false;
            }
            pixels = payload;
            start = header.DataOffset;
            problem = string.Empty;
            return true;
        }

        if (!PackageLzma.TryReadHeader(body, out long decoded, out _) || decoded != expected)
        {
            pixels = Array.Empty<byte>();
            start = 0;
            problem = $"compressed pixels declare {decoded} bytes, its size and format need {expected}";
            return false;
        }

        pixels = PackageLzma.Decode(body);
        start = 0;
        problem = string.Empty;
        return true;
    }

    // Each panorama pixel turns into a direction here, crosses into package space (the X mirror every
    // package position gets, so the sky stays behind the same buildings) and samples the cube there.
    // Bilinear within a face, clamped at its edges: at four face widths around, a panorama pixel is
    // about one texel, so edge clamping never shows as a seam. -xlinka
    private static byte[] CubeToPanorama(byte[] pixels, int start, in Header header, int bytesPerPixel, bool hdr)
    {
        int faceSize = header.Width;
        int faceOut = System.Math.Min(faceSize, MaxPanoramaFace);
        int width = faceOut * 4;
        int height = faceOut * 2;
        long faceBytes = LayerPixels(faceSize, faceSize, header.Mipmaps) * bytesPerPixel;
        var format = header.Format;
        bool encode = !hdr && header.Profile == ColorProfile.Linear;
        bool linearize = hdr && header.Profile != ColorProfile.Linear;
        var rgba = hdr ? null : new byte[(long)width * height * 4];
        var rgb = hdr ? new float[(long)width * height * 3] : null;

        Parallel.For(0, height, y =>
        {
            float v = (y + 0.5f) / height;
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                var direction = PackageHandedness.Point(CubemapProjector.PanoramaUvToDirection(u, v));
                var value = SampleCube(pixels, start, faceBytes, faceSize, bytesPerPixel, format, direction);
                if (rgb != null)
                {
                    if (linearize)
                        value = value.ToLinear();
                    long at = (row + x) * 3;
                    rgb[at] = value.r;
                    rgb[at + 1] = value.g;
                    rgb[at + 2] = value.b;
                    continue;
                }
                if (encode)
                    value = value.ToGamma();
                WriteByteColor(rgba!, (int)((row + x) * 4), value);
            }
        });

        return rgb != null ? WriteRadiance(rgb, width, height) : PhotoEncoder.Png(rgba!, width, height);
    }

    private static color SampleCube(byte[] pixels, int start, long faceBytes, int faceSize, int bytesPerPixel,
        PixelFormat format, in float3 direction)
    {
        var face = CubemapProjector.DirectionToFace(direction, out float s, out float t);
        float fx = s * faceSize - 0.5f;
        float fy = t * faceSize - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        int x1 = System.Math.Clamp(x0 + 1, 0, faceSize - 1);
        int y1 = System.Math.Clamp(y0 + 1, 0, faceSize - 1);
        x0 = System.Math.Clamp(x0, 0, faceSize - 1);
        y0 = System.Math.Clamp(y0, 0, faceSize - 1);

        long origin = start + (int)face * faceBytes;
        var c00 = ReadTexel(pixels, origin + ((long)y0 * faceSize + x0) * bytesPerPixel, format);
        var c10 = ReadTexel(pixels, origin + ((long)y0 * faceSize + x1) * bytesPerPixel, format);
        var c01 = ReadTexel(pixels, origin + ((long)y1 * faceSize + x0) * bytesPerPixel, format);
        var c11 = ReadTexel(pixels, origin + ((long)y1 * faceSize + x1) * bytesPerPixel, format);

        var top = color.Lerp(c00, c10, tx);
        var bottom = color.Lerp(c01, c11, tx);
        return color.Lerp(top, bottom, ty);
    }

    private static byte[] FlatToPng(byte[] pixels, int start, in Header header, int bytesPerPixel)
    {
        int width = header.Width;
        int height = header.Height;
        var rgba = new byte[(long)width * height * 4];
        for (int y = 0; y < height; y++)
        {
            long source = start + (long)SourceRow(header, y) * width * bytesPerPixel;
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
                WriteByteColor(rgba, row + x * 4, ReadTexel(pixels, source + (long)x * bytesPerPixel, header.Format));
        }
        return PhotoEncoder.Png(rgba, width, height);
    }

    // Radiance holds linear light and no alpha.
    private static byte[] FlatToRadiance(byte[] pixels, int start, in Header header, int bytesPerPixel)
    {
        int width = header.Width;
        int height = header.Height;
        bool linearize = header.Profile != ColorProfile.Linear;
        var rgb = new float[(long)width * height * 3];
        for (int y = 0; y < height; y++)
        {
            long source = start + (long)SourceRow(header, y) * width * bytesPerPixel;
            long row = (long)y * width * 3;
            for (int x = 0; x < width; x++)
            {
                var value = ReadTexel(pixels, source + (long)x * bytesPerPixel, header.Format);
                if (linearize)
                    value = value.ToLinear();
                rgb[row + x * 3] = value.r;
                rgb[row + x * 3 + 1] = value.g;
                rgb[row + x * 3 + 2] = value.b;
            }
        }
        return WriteRadiance(rgb, width, height);
    }

    private static int SourceRow(in Header header, int y) => header.BottomUp ? header.Height - 1 - y : y;

    // Channel order and the 565 bit layout follow the package's own pixel reader, missing channels
    // read as it reads them: 0 for colour, 1 for alpha.
    private static color ReadTexel(byte[] data, long offset, PixelFormat format)
    {
        var p = data.AsSpan((int)offset);
        switch (format)
        {
            case PixelFormat.Alpha8:
                return new color(0f, 0f, 0f, p[0] / 255f);
            case PixelFormat.R8:
                return new color(p[0] / 255f, 0f, 0f, 1f);
            case PixelFormat.RGB24:
                return new color(p[0] / 255f, p[1] / 255f, p[2] / 255f, 1f);
            case PixelFormat.ARGB32:
                return new color(p[1] / 255f, p[2] / 255f, p[3] / 255f, p[0] / 255f);
            case PixelFormat.RGBA32:
                return new color(p[0] / 255f, p[1] / 255f, p[2] / 255f, p[3] / 255f);
            case PixelFormat.BGRA32:
                return new color(p[2] / 255f, p[1] / 255f, p[0] / 255f, p[3] / 255f);
            case PixelFormat.RGB565:
            {
                int v = BinaryPrimitives.ReadUInt16LittleEndian(p);
                return new color((v & 0x1F) / 31f, ((v >> 5) & 0x3F) / 63f, ((v >> 11) & 0x1F) / 31f, 1f);
            }
            case PixelFormat.BGR565:
            {
                int v = BinaryPrimitives.ReadUInt16LittleEndian(p);
                return new color(((v >> 11) & 0x1F) / 31f, ((v >> 5) & 0x3F) / 63f, (v & 0x1F) / 31f, 1f);
            }
            case PixelFormat.RGBAHalf:
                return new color(ReadHalf(p, 0), ReadHalf(p, 2), ReadHalf(p, 4), ReadHalf(p, 6));
            case PixelFormat.ARGBHalf:
                return new color(ReadHalf(p, 2), ReadHalf(p, 4), ReadHalf(p, 6), ReadHalf(p, 0));
            case PixelFormat.RHalf:
                return new color(ReadHalf(p, 0), 0f, 0f, 1f);
            case PixelFormat.RGHalf:
                return new color(ReadHalf(p, 0), ReadHalf(p, 2), 0f, 1f);
            case PixelFormat.RGBAFloat:
                return new color(ReadSingle(p, 0), ReadSingle(p, 4), ReadSingle(p, 8), ReadSingle(p, 12));
            case PixelFormat.ARGBFloat:
                return new color(ReadSingle(p, 4), ReadSingle(p, 8), ReadSingle(p, 12), ReadSingle(p, 0));
            case PixelFormat.RFloat:
                return new color(ReadSingle(p, 0), 0f, 0f, 1f);
            case PixelFormat.RGFloat:
                return new color(ReadSingle(p, 0), ReadSingle(p, 4), 0f, 1f);
            default:
                throw new InvalidDataException($"{format} pixels cannot be read");
        }
    }

    // NaN and infinity are cleaned up where they enter, so neither clamping nor RGBE has to care.
    private static float ReadHalf(ReadOnlySpan<byte> p, int at) =>
        Finite((float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(at))));

    private static float ReadSingle(ReadOnlySpan<byte> p, int at) =>
        Finite(BinaryPrimitives.ReadSingleLittleEndian(p.Slice(at)));

    private static float Finite(float value)
    {
        if (float.IsNaN(value))
            return 0f;
        if (float.IsInfinity(value))
            return value > 0f ? 65504f : -65504f;
        return value;
    }

    private static void WriteByteColor(byte[] rgba, int index, in color value)
    {
        rgba[index] = ToByte(value.r);
        rgba[index + 1] = ToByte(value.g);
        rgba[index + 2] = ToByte(value.b);
        rgba[index + 3] = ToByte(value.a);
    }

    private static byte ToByte(float value)
    {
        if (!(value > 0f))
            return 0;
        if (value >= 1f)
            return 255;
        return (byte)(value * 255f + 0.5f);
    }

    // Radiance RGBE with the run-length scanlines every reader expects for widths 8..32767.
    private static byte[] WriteRadiance(float[] rgb, int width, int height)
    {
        using var output = new MemoryStream();
        var head = Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n");
        output.Write(head);

        bool runLength = width >= RadianceMinWidth && width < 32768;
        var scanline = new byte[width * 4];
        var channel = new byte[width];
        for (int y = 0; y < height; y++)
        {
            long row = (long)y * width * 3;
            for (int x = 0; x < width; x++)
                ToRgbe(rgb[row + x * 3], rgb[row + x * 3 + 1], rgb[row + x * 3 + 2], scanline, x * 4);

            if (!runLength)
            {
                output.Write(scanline);
                continue;
            }

            output.WriteByte(2);
            output.WriteByte(2);
            output.WriteByte((byte)(width >> 8));
            output.WriteByte((byte)width);
            for (int c = 0; c < 4; c++)
            {
                for (int x = 0; x < width; x++)
                    channel[x] = scanline[x * 4 + c];
                WriteRunLengthChannel(output, channel);
            }
        }
        return output.ToArray();
    }

    // Runs of three or more repeat as (128 + count, value); everything between goes out as literal
    // spans of up to 128.
    private static void WriteRunLengthChannel(Stream output, byte[] channel)
    {
        int width = channel.Length;
        int x = 0;
        while (x < width)
        {
            int run = x;
            while (run + 2 < width && !(channel[run] == channel[run + 1] && channel[run] == channel[run + 2]))
                run++;
            if (run + 2 >= width)
                run = width;

            while (x < run)
            {
                int count = System.Math.Min(run - x, 128);
                output.WriteByte((byte)count);
                output.Write(channel, x, count);
                x += count;
            }

            if (run < width)
            {
                byte value = channel[run];
                int stop = run;
                while (stop < width && channel[stop] == value)
                    stop++;
                while (x < stop)
                {
                    int count = System.Math.Min(stop - x, 127);
                    output.WriteByte((byte)(128 + count));
                    output.WriteByte(value);
                    x += count;
                }
            }
        }
    }

    private static void ToRgbe(float r, float g, float b, byte[] target, int at)
    {
        r = System.Math.Max(r, 0f);
        g = System.Math.Max(g, 0f);
        b = System.Math.Max(b, 0f);
        float max = System.Math.Max(r, System.Math.Max(g, b));
        if (max < 1e-32f)
        {
            target[at] = target[at + 1] = target[at + 2] = target[at + 3] = 0;
            return;
        }
        int exponent = MathF.ILogB(max) + 1;
        float scale = MathF.ScaleB(256f, -exponent);
        target[at] = (byte)System.Math.Min(255f, r * scale);
        target[at + 1] = (byte)System.Math.Min(255f, g * scale);
        target[at + 2] = (byte)System.Math.Min(255f, b * scale);
        target[at + 3] = (byte)(exponent + 128);
    }

    private static bool TryReadCubeFormat(ReadOnlySpan<byte> payload, ref int offset, int version, out PixelFormat format)
    {
        format = PixelFormat.Unknown;
        if (version >= 2)
        {
            if (!TryReadUInt16(payload, ref offset, out ushort ordinal))
                return false;
            format = (PixelFormat)ordinal;
            return true;
        }
        if (version == 1)
            return TryReadString(payload, ref offset, 32, out var name) && Enum.TryParse(name, out format);

        if (!TryReadVarInt(payload, ref offset, out int legacy))
            return false;
        format = legacy switch
        {
            1 => PixelFormat.RGB24,
            2 => PixelFormat.ARGB32,
            3 => PixelFormat.RGBA32,
            4 => PixelFormat.BGRA32,
            5 => PixelFormat.RGBAHalf,
            6 => PixelFormat.RGBAFloat,
            _ => PixelFormat.Unknown,
        };
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> source, ref int offset, int maxLength, out string value)
    {
        value = string.Empty;
        if (!TryReadVarInt(source, ref offset, out int length) || length < 0 || length > maxLength)
            return false;
        if (offset + length > source.Length)
            return false;
        value = Encoding.UTF8.GetString(source.Slice(offset, length));
        offset += length;
        return true;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> source, ref int offset, out int value)
    {
        value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (offset >= source.Length)
                return false;
            byte b = source[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
        }
        return false;
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> source, ref int offset, out int value)
    {
        value = 0;
        if (offset + 4 > source.Length)
            return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
        offset += 4;
        return true;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> source, ref int offset, out ushort value)
    {
        value = 0;
        if (offset + 2 > source.Length)
            return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset));
        offset += 2;
        return true;
    }

    private static bool TryReadByte(ReadOnlySpan<byte> source, ref int offset, out byte value)
    {
        value = 0;
        if (offset >= source.Length)
            return false;
        value = source[offset++];
        return true;
    }
}
