// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Core.Assets;

// The uncompressed entries describe how many channels the content actually USES (measured by scanning the
// decoded pixels), the BC entries describe a block-compressed GPU resource.
public enum TextureFormatKind
{
    Unknown = 0,
    R8,
    RG8,
    RGB8,
    RGBA8,
    BC1_RGB,
    BC3_RGBA,
    BC4_R,
    BC5_RG,
    BC7_RGBA,
}

public static class TextureFormatKindExtensions
{
    // 4x4 blocks.
    public static bool IsBlockCompressed(this TextureFormatKind format) => format switch
    {
        TextureFormatKind.BC1_RGB or TextureFormatKind.BC3_RGBA or TextureFormatKind.BC4_R
            or TextureFormatKind.BC5_RG or TextureFormatKind.BC7_RGBA => true,
        _ => false,
    };

    // 0 when the format is not block compressed.
    public static int BlockBytes(this TextureFormatKind format) => format switch
    {
        TextureFormatKind.BC1_RGB or TextureFormatKind.BC4_R => 8,
        TextureFormatKind.BC3_RGBA or TextureFormatKind.BC5_RG or TextureFormatKind.BC7_RGBA => 16,
        _ => 0,
    };

    // 0 when block compressed or unknown.
    public static int BytesPerPixel(this TextureFormatKind format) => format switch
    {
        TextureFormatKind.R8 => 1,
        TextureFormatKind.RG8 => 2,
        TextureFormatKind.RGB8 => 3,
        TextureFormatKind.RGBA8 => 4,
        _ => 0,
    };

    public static bool HasAlphaChannel(this TextureFormatKind format) => format switch
    {
        TextureFormatKind.RGBA8 or TextureFormatKind.BC3_RGBA or TextureFormatKind.BC7_RGBA => true,
        _ => false,
    };

    public static string Label(this TextureFormatKind format) => format switch
    {
        TextureFormatKind.BC1_RGB => "BC1",
        TextureFormatKind.BC3_RGBA => "BC3",
        TextureFormatKind.BC4_R => "BC4",
        TextureFormatKind.BC5_RG => "BC5",
        TextureFormatKind.BC7_RGBA => "BC7",
        TextureFormatKind.Unknown => "unknown",
        _ => format.ToString(),
    };
}

// Measured facts about one texture: what was decoded, what the pixels actually contain, and what
// the renderer ended up holding. Nothing here is guessed - alpha and channel usage come from a
// scan of the decoded pixels, sizes are counted bytes, and the GPU format is reported back by the
// renderer after the upload actually happened. Fields we cannot determine stay null rather than
// being filled with a plausible default, because a wrong VRAM figure is worse than no figure.
//
// Rides with the asset two ways: as key/values on the local asset record (so a re-open needs no
// decode) and as a small sidecar blob addressed by its own local:// URI (so a peer can read the
// facts before, or instead of, transferring pixels). -xlinka
public sealed class TextureMetadata
{
    public int Width { get; init; }

    public int Height { get; init; }

    public TextureFormatKind ContentFormat { get; init; } = TextureFormatKind.Unknown;

    // Null until the renderer reports it back - the engine side never assumes a GPU format it did not observe.
    public TextureFormatKind? GpuFormat { get; set; }

    // Scanned, never assumed.
    public bool HasAlpha { get; init; }

    // 1 = base level only.
    public int MipCount { get; init; } = 1;

    public long SourceBytes { get; init; }

    // Mips included.
    public long DecodedBytes { get; init; }

    // Not inferred from usage.
    public bool? SRgb { get; init; }

    // Never guessed from file name or pixel statistics.
    public bool IsNormalMap { get; init; }

    // Null for the base asset.
    public string? VariantId { get; init; }

    // Computed from the reported GPU format and the real dimensions/mip count. Null while GpuFormat
    // is unknown.
    public long? GpuBytes =>
        GpuFormat is { } format ? ComputeGpuBytes(format, Width, Height, MipCount) : null;

    // Block formats round each level up to whole 4x4 blocks, which is what the driver allocates.
    public static long ComputeGpuBytes(TextureFormatKind format, int width, int height, int mipCount)
    {
        if (width <= 0 || height <= 0 || mipCount <= 0)
            return 0;

        long total = 0;
        int w = width, h = height;
        for (int level = 0; level < mipCount; level++)
        {
            total += LevelBytes(format, w, h);
            if (w == 1 && h == 1)
                break;
            w = System.Math.Max(1, w >> 1);
            h = System.Math.Max(1, h >> 1);
        }
        return total;
    }

    public static long LevelBytes(TextureFormatKind format, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0;

        if (format.IsBlockCompressed())
        {
            long blocksX = (width + 3) / 4;
            long blocksY = (height + 3) / 4;
            return blocksX * blocksY * format.BlockBytes();
        }

        int bpp = format.BytesPerPixel();
        return bpp <= 0 ? 0 : (long)width * height * bpp;
    }

    public static int FullMipCount(int width, int height)
    {
        int levels = 1;
        int w = System.Math.Max(1, width), h = System.Math.Max(1, height);
        while (w > 1 || h > 1)
        {
            w = System.Math.Max(1, w >> 1);
            h = System.Math.Max(1, h >> 1);
            levels++;
        }
        return levels;
    }

    // One pass reads alpha and channel equality together; the result decides ContentFormat, which is the only
    // place the "is this really RGBA" question gets answered honestly.
    public static TextureMetadata Analyze(
        byte[] rgba,
        int width,
        int height,
        int mipCount,
        long sourceBytes,
        long decodedBytes,
        bool? srgb,
        bool isNormalMap,
        string? variantId = null)
    {
        bool hasAlpha = false;
        bool grayscale = true;
        bool blueUnused = true;

        int pixels = width * height;
        if (rgba != null && pixels > 0 && rgba.Length >= pixels * 4)
        {
            for (int i = 0, o = 0; i < pixels; i++, o += 4)
            {
                byte r = rgba[o], g = rgba[o + 1], b = rgba[o + 2], a = rgba[o + 3];
                if (a != 255)
                    hasAlpha = true;
                if (grayscale && (r != g || g != b))
                    grayscale = false;
                if (blueUnused && b != 0)
                    blueUnused = false;
                if (hasAlpha && !grayscale && !blueUnused)
                    break;
            }
        }
        else
        {
            // Nothing to scan: report the layout we hold rather than inventing channel facts.
            grayscale = false;
            blueUnused = false;
        }

        TextureFormatKind content;
        if (hasAlpha)
            content = TextureFormatKind.RGBA8;
        else if (grayscale)
            content = TextureFormatKind.R8;
        else if (blueUnused)
            content = TextureFormatKind.RG8;
        else
            content = TextureFormatKind.RGB8;

        return new TextureMetadata
        {
            Width = width,
            Height = height,
            ContentFormat = content,
            HasAlpha = hasAlpha,
            MipCount = System.Math.Max(1, mipCount),
            SourceBytes = sourceBytes,
            DecodedBytes = decodedBytes,
            SRgb = srgb,
            IsNormalMap = isNormalMap,
            VariantId = variantId,
        };
    }

    // PNG carries sRGB / iCCP / gAMA chunks; JPEG carries an APP2 ICC_PROFILE segment. Anything else, or a file
    // that says nothing, returns null - unknown stays unknown.
    public static bool? DetectSRgb(byte[] encoded)
    {
        if (encoded == null || encoded.Length < 16)
            return null;

        // PNG: 8-byte signature, then length-prefixed chunks.
        if (encoded[0] == 0x89 && encoded[1] == 0x50 && encoded[2] == 0x4E && encoded[3] == 0x47)
        {
            int offset = 8;
            while (offset + 8 <= encoded.Length)
            {
                long length = ((long)encoded[offset] << 24) | ((long)encoded[offset + 1] << 16)
                    | ((long)encoded[offset + 2] << 8) | encoded[offset + 3];
                if (length < 0 || length > int.MaxValue - 12)
                    return null;

                string type = string.Concat(
                    (char)encoded[offset + 4], (char)encoded[offset + 5],
                    (char)encoded[offset + 6], (char)encoded[offset + 7]);

                switch (type)
                {
                    case "sRGB":
                    case "iCCP":
                        return true;
                    case "gAMA":
                        if (offset + 12 <= encoded.Length)
                        {
                            long gamma = ((long)encoded[offset + 8] << 24) | ((long)encoded[offset + 9] << 16)
                                | ((long)encoded[offset + 10] << 8) | encoded[offset + 11];
                            // 45455 = 1/2.2 in PNG's 1/100000 fixed point; anything near it is sRGB-ish.
                            return System.Math.Abs(gamma - 45455) < 1000;
                        }
                        return null;
                    case "IDAT":
                    case "IEND":
                        return null; // color-space chunks precede the data; none were present
                }

                offset += 12 + (int)length; // length + type + data + crc
            }
            return null;
        }

        // JPEG: SOI then marker segments; an APP2 ICC_PROFILE means a declared profile.
        if (encoded[0] == 0xFF && encoded[1] == 0xD8)
        {
            int offset = 2;
            while (offset + 4 <= encoded.Length && encoded[offset] == 0xFF)
            {
                byte marker = encoded[offset + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    offset += 2;
                    continue;
                }
                if (marker == 0xDA || marker == 0xD9)
                    return null; // reached image data, nothing declared

                int segment = (encoded[offset + 2] << 8) | encoded[offset + 3];
                if (segment < 2)
                    return null;

                if (marker == 0xE2 && offset + 4 + 4 <= encoded.Length)
                {
                    bool icc = encoded[offset + 4] == (byte)'I' && encoded[offset + 5] == (byte)'C'
                        && encoded[offset + 6] == (byte)'C' && encoded[offset + 7] == (byte)'_';
                    if (icc)
                        return true;
                }

                offset += 2 + segment;
            }
        }

        return null;
    }

    // PERSISTENCE

    private const string KeyPrefix = "tex.";

    public void WriteTo(IDictionary<string, string> bag)
    {
        if (bag == null)
            return;
        bag[KeyPrefix + "width"] = Width.ToString(CultureInfo.InvariantCulture);
        bag[KeyPrefix + "height"] = Height.ToString(CultureInfo.InvariantCulture);
        bag[KeyPrefix + "format"] = ContentFormat.ToString();
        bag[KeyPrefix + "hasAlpha"] = HasAlpha ? "1" : "0";
        bag[KeyPrefix + "mips"] = MipCount.ToString(CultureInfo.InvariantCulture);
        bag[KeyPrefix + "sourceBytes"] = SourceBytes.ToString(CultureInfo.InvariantCulture);
        bag[KeyPrefix + "decodedBytes"] = DecodedBytes.ToString(CultureInfo.InvariantCulture);
        bag[KeyPrefix + "normalMap"] = IsNormalMap ? "1" : "0";
        if (SRgb.HasValue)
            bag[KeyPrefix + "srgb"] = SRgb.Value ? "1" : "0";
        else
            bag.Remove(KeyPrefix + "srgb");
        if (!string.IsNullOrEmpty(VariantId))
            bag[KeyPrefix + "variant"] = VariantId!;
    }

    // Null if absent.
    public static TextureMetadata? ReadFrom(IReadOnlyDictionary<string, string>? bag)
    {
        if (bag == null || !bag.TryGetValue(KeyPrefix + "width", out var widthRaw))
            return null;

        int ReadInt(string key, int fallback) =>
            bag.TryGetValue(KeyPrefix + key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : fallback;

        long ReadLong(string key) =>
            bag.TryGetValue(KeyPrefix + key, out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : 0L;

        bool ReadBool(string key) => bag.TryGetValue(KeyPrefix + key, out var raw) && raw == "1";

        if (!int.TryParse(widthRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
            return null;

        bool? srgb = bag.TryGetValue(KeyPrefix + "srgb", out var srgbRaw) ? srgbRaw == "1" : null;
        var format = bag.TryGetValue(KeyPrefix + "format", out var formatRaw)
            && Enum.TryParse<TextureFormatKind>(formatRaw, out var parsed)
                ? parsed : TextureFormatKind.Unknown;

        return new TextureMetadata
        {
            Width = width,
            Height = ReadInt("height", 0),
            ContentFormat = format,
            HasAlpha = ReadBool("hasAlpha"),
            MipCount = System.Math.Max(1, ReadInt("mips", 1)),
            SourceBytes = ReadLong("sourceBytes"),
            DecodedBytes = ReadLong("decodedBytes"),
            SRgb = srgb,
            IsNormalMap = ReadBool("normalMap"),
            VariantId = bag.TryGetValue(KeyPrefix + "variant", out var variant) ? variant : null,
        };
    }

    // The standalone sidecar blob peers fetch instead of decoding.
    public byte[] ToSidecarBytes()
    {
        var bag = new Dictionary<string, string>(StringComparer.Ordinal);
        WriteTo(bag);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bag);
    }

    public static TextureMetadata? FromSidecarBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        try
        {
            var bag = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(bytes);
            return ReadFrom(bag);
        }
        catch
        {
            return null;
        }
    }

    public string DescribeFormat()
    {
        string content = ContentFormat.Label();
        if (GpuFormat is { } gpu && gpu != ContentFormat)
            return $"{content} -> {gpu.Label()}";
        return content;
    }
}
