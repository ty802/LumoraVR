// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Lumora.Core.Assets;

// Baseline TIFF to straight top-down RGBA8, the same contract as every decoder behind DecodeRgba.
// First image only. Strips or tiles, chunky or planar, either byte order. Compression: none, LZW
// (both bit orders), Deflate, PackBits. Predictors: horizontal (8 to 64 bit) and floating point.
// Samples: 1/2/4/8/16/32 bit unsigned, 16/32/64 bit float clamped to 0..1 as stored. Gray (either
// polarity), RGB, palette and CMYK, with straight or premultiplied alpha. Anything else throws
// and names what it was. -xlinka
public static class TiffDecoder
{
    private const int MaxSide = 32768;
    private const long MaxPixels = 16384L * 16384L;

    public static bool IsTiff(ReadOnlySpan<byte> data)
        => data.Length >= 8
           && ((data[0] == 'I' && data[1] == 'I' && (data[2] == 42 || data[2] == 43) && data[3] == 0)
               || (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && (data[3] == 42 || data[3] == 43)));

    public static byte[] Decode(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data == null || !IsTiff(data))
            throw new InvalidDataException("TIFF: not a TIFF file");

        bool big = data[0] == 'M';
        if ((big ? data[3] : data[2]) == 43)
            throw new NotSupportedException("TIFF: BigTIFF files are not supported");

        var ifd = new Ifd(data, big);

        long w = ifd.Int(256, 0);
        long h = ifd.Int(257, 0);
        if (w <= 0 || h <= 0)
            throw new InvalidDataException("TIFF: image has no size");
        if (w > MaxSide || h > MaxSide || w * h > MaxPixels)
            throw new NotSupportedException($"TIFF: {w}x{h} is larger than a texture can be");

        int spp = (int)ifd.Int(277, 1);
        if (spp < 1 || spp > 32)
            throw new InvalidDataException($"TIFF: {spp} samples per pixel");

        int bps = Uniform(ifd.Ints(258), spp, 1, "bits per sample");
        int format = Uniform(ifd.Ints(339), spp, 1, "sample formats");
        int compression = (int)ifd.Int(259, 1);
        int photometric = (int)ifd.Int(262, spp >= 3 ? 2 : 1);
        int planar = spp == 1 ? 1 : (int)ifd.Int(284, 1);
        int predictor = (int)ifd.Int(317, 1);
        int fillOrder = (int)ifd.Int(266, 1);

        if (compression is not (1 or 5 or 8 or 32946 or 32773))
            throw new NotSupportedException($"TIFF: {CompressionName(compression)} compression is not supported");

        int colorSamples = photometric switch
        {
            0 or 1 or 3 => 1,
            2 => 3,
            5 => 4,
            _ => throw new NotSupportedException($"TIFF: {PhotometricName(photometric)} images are not supported"),
        };
        if (photometric == 5 && ifd.Int(332, 1) != 1)
            throw new NotSupportedException("TIFF: separated images other than CMYK are not supported");
        if (spp < colorSamples)
            throw new InvalidDataException($"TIFF: {spp} samples per pixel is too few for {PhotometricName(photometric)}");
        if (planar is not (1 or 2))
            throw new InvalidDataException($"TIFF: planar configuration {planar}");

        if (format == 1)
        {
            if (bps is not (1 or 2 or 4 or 8 or 16 or 32))
                throw new NotSupportedException($"TIFF: {bps}-bit samples are not supported");
            if (bps < 8 && spp != 1)
                throw new NotSupportedException($"TIFF: {bps}-bit samples are only supported in single-channel images");
        }
        else if (format == 3)
        {
            if (bps is not (16 or 32 or 64))
                throw new NotSupportedException($"TIFF: {bps}-bit float samples are not supported");
        }
        else
        {
            throw new NotSupportedException(format == 2
                ? "TIFF: signed integer samples are not supported"
                : $"TIFF: sample format {format} is not supported");
        }

        if (photometric == 3 && (format != 1 || bps > 8))
            throw new NotSupportedException("TIFF: palette images deeper than 8 bits are not supported");
        if (predictor is not (1 or 2 or 3))
            throw new NotSupportedException($"TIFF: predictor {predictor} is not supported");
        if (predictor == 2 && bps < 8)
            throw new NotSupportedException("TIFF: horizontal predictor on sub-byte samples is not supported");
        if (predictor == 3 && format != 3)
            throw new InvalidDataException("TIFF: floating point predictor on integer samples");

        // Sample index to RGBA slot. Color lands in its own slots and is converted in place at the end;
        // the first extra sample is alpha when it says so, or when nothing says what it is.
        var map = new int[spp];
        Array.Fill(map, -1);
        for (int s = 0; s < colorSamples; s++)
            map[s] = s;
        bool hasAlpha = false;
        bool premultiplied = false;
        if (spp > colorSamples && photometric != 5)
        {
            var extra = ifd.Ints(338);
            long kind = extra != null && extra.Length > 0 ? extra[0] : 2;
            if (kind is 1 or 2)
            {
                map[colorSamples] = 3;
                hasAlpha = true;
                premultiplied = kind == 1;
            }
        }

        byte[]? palette = photometric == 3 ? BuildPalette(ifd.Ints(320), bps) : null;

        bool tiled = ifd.Has(322) || ifd.Has(324);
        long chunkW, chunkH;
        long[]? offsets, counts;
        if (tiled)
        {
            chunkW = ifd.Int(322, 0);
            chunkH = ifd.Int(323, 0);
            offsets = ifd.Ints(324);
            counts = ifd.Ints(325);
            if (chunkW <= 0 || chunkH <= 0 || chunkW > MaxSide || chunkH > MaxSide)
                throw new InvalidDataException($"TIFF: tile size {chunkW}x{chunkH}");
        }
        else
        {
            chunkW = w;
            long rps = ifd.Int(278, h);
            chunkH = rps <= 0 || rps > h ? h : rps;
            offsets = ifd.Ints(273);
            counts = ifd.Ints(279);
        }
        if (offsets == null)
            throw new InvalidDataException("TIFF: no image data offsets");

        long across = (w + chunkW - 1) / chunkW;
        long down = (h + chunkH - 1) / chunkH;
        int planes = planar == 2 ? spp : 1;
        int spc = planar == 2 ? 1 : spp;
        long perPlane = across * down;
        if (offsets.Length < perPlane * planes)
            throw new InvalidDataException($"TIFF: {offsets.Length} data blocks where {perPlane * planes} are needed");

        long rowBytes = (chunkW * spc * bps + 7) / 8;
        if (rowBytes * chunkH > Array.MaxLength)
            throw new NotSupportedException("TIFF: a single strip or tile is too large");

        var buffer = new byte[rowBytes * chunkH];
        var rgba = new byte[w * h * 4];
        var lzw = compression == 5 ? new LzwTable() : null;
        int sampleBytes = bps / 8;
        bool swap = big && bps > 8 && predictor != 3;
        byte[]? floatRow = predictor == 3 ? new byte[rowBytes] : null;
        byte[]? reversed = null;
        var chunkMap = new int[spc];

        for (int plane = 0; plane < planes; plane++)
        {
            if (planar == 2)
            {
                chunkMap[0] = map[plane];
                if (chunkMap[0] < 0)
                    continue;
            }
            else
            {
                Array.Copy(map, chunkMap, spp);
            }

            for (long ty = 0; ty < down; ty++)
            {
                for (long tx = 0; tx < across; tx++)
                {
                    long index = plane * perPlane + ty * across + tx;
                    long y0 = ty * chunkH;
                    long x0 = tx * chunkW;
                    long rows = tiled ? chunkH : System.Math.Min(chunkH, h - y0);
                    int expected = (int)(rows * rowBytes);
                    var dst = buffer.AsSpan(0, expected);

                    int produced = 0;
                    long offset = offsets[index];
                    if (offset > 0 && offset < data.Length)
                    {
                        long count = counts != null && index < counts.Length && counts[index] > 0
                            ? counts[index]
                            : compression == 1 ? expected : data.Length - offset;
                        int length = (int)System.Math.Min(count, data.Length - offset);
                        byte[] source = data;
                        int start = (int)offset;
                        if (fillOrder == 2)
                        {
                            if (reversed == null || reversed.Length < length)
                                reversed = new byte[length];
                            for (int i = 0; i < length; i++)
                                reversed[i] = ReverseBits(data[start + i]);
                            source = reversed;
                            start = 0;
                        }
                        produced = Decompress(compression, source, start, length, dst, lzw);
                    }
                    if (produced < expected)
                        dst.Slice(produced).Clear();

                    if (swap)
                        Swap(dst, sampleBytes);
                    if (predictor == 2)
                        UndoHorizontal(dst, rowBytes, rows, chunkW * spc, spc, sampleBytes);
                    else if (predictor == 3)
                        UndoFloat(dst, rowBytes, rows, chunkW * spc, spc, sampleBytes, floatRow!);

                    Place(buffer, rowBytes, rows, x0, y0, chunkW, (int)w, (int)h, spc, chunkMap, bps, format == 3,
                        photometric != 3, rgba);
                }
            }
        }

        Finish(rgba, photometric, palette, hasAlpha, premultiplied);
        width = (int)w;
        height = (int)h;
        return rgba;
    }

    // DIRECTORY

    private sealed class Ifd
    {
        private readonly byte[] _data;
        private readonly bool _big;
        private readonly Dictionary<int, (int Type, long Count, long Pos)> _tags = new();

        public Ifd(byte[] data, bool big)
        {
            _data = data;
            _big = big;
            long offset = U32(4);
            if (offset < 8 || offset + 2 > data.Length)
                throw new InvalidDataException("TIFF: first directory lies outside the file");
            int n = U16(offset);
            long pos = offset + 2;
            for (int i = 0; i < n && pos + 12 <= data.Length; i++, pos += 12)
            {
                int tag = U16(pos);
                int type = U16(pos + 2);
                long count = U32(pos + 4);
                int size = TypeSize(type);
                if (size == 0)
                    continue;
                long valuePos = count * size <= 4 ? pos + 8 : U32(pos + 8);
                _tags.TryAdd(tag, (type, count, valuePos));
            }
        }

        public bool Has(int tag) => _tags.ContainsKey(tag);

        public long Int(int tag, long fallback)
        {
            var values = Ints(tag);
            return values != null && values.Length > 0 ? values[0] : fallback;
        }

        public long[]? Ints(int tag)
        {
            if (!_tags.TryGetValue(tag, out var entry))
                return null;
            int size = TypeSize(entry.Type);
            if (entry.Pos < 0 || entry.Pos + entry.Count * size > _data.Length)
                throw new InvalidDataException($"TIFF: tag {tag} runs past the end of the file");
            var values = new long[entry.Count];
            long p = entry.Pos;
            for (long i = 0; i < entry.Count; i++, p += size)
            {
                values[i] = entry.Type switch
                {
                    1 or 7 => _data[p],
                    6 => (sbyte)_data[p],
                    3 => U16(p),
                    8 => (short)U16(p),
                    4 or 13 => U32(p),
                    9 => (int)U32(p),
                    _ => throw new InvalidDataException($"TIFF: tag {tag} is not an integer"),
                };
            }
            return values;
        }

        private int U16(long p)
        {
            if (p < 0 || p + 2 > _data.Length)
                throw new InvalidDataException("TIFF: file is truncated");
            var span = _data.AsSpan((int)p, 2);
            return _big ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
        }

        private long U32(long p)
        {
            if (p < 0 || p + 4 > _data.Length)
                throw new InvalidDataException("TIFF: file is truncated");
            var span = _data.AsSpan((int)p, 4);
            return _big ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
        }

        private static int TypeSize(int type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 or 13 => 4,
            5 or 10 or 12 => 8,
            _ => 0,
        };
    }

    private static int Uniform(long[]? values, int spp, int fallback, string what)
    {
        if (values == null || values.Length == 0)
            return fallback;
        long first = values[0];
        for (int i = 1; i < System.Math.Min(spp, values.Length); i++)
        {
            if (values[i] != first)
                throw new NotSupportedException($"TIFF: mixed {what} ({string.Join(",", values)}) are not supported");
        }
        return (int)first;
    }

    private static string CompressionName(int compression) => compression switch
    {
        2 => "CCITT modified Huffman",
        3 => "CCITT Group 3 fax",
        4 => "CCITT Group 4 fax",
        6 => "old-style JPEG",
        7 => "JPEG",
        34712 => "JPEG 2000",
        34887 => "LERC",
        34925 => "LZMA",
        50000 => "Zstandard",
        50001 => "WebP",
        _ => $"type {compression}",
    };

    private static string PhotometricName(int photometric) => photometric switch
    {
        0 or 1 => "grayscale",
        2 => "RGB",
        3 => "palette",
        4 => "transparency mask",
        5 => "CMYK",
        6 => "YCbCr",
        8 or 9 or 10 => "L*a*b*",
        32803 => "color filter array",
        32844 or 32845 => "LogLuv",
        34892 => "linear raw",
        _ => $"photometric {photometric}",
    };

    // A color map is 16 bits per entry, red block then green then blue. Writers scale 8-bit colors up
    // by 256 or by 257, and the high byte is the color either way; a map with nothing above 255 was
    // written as 8-bit values outright.
    private static byte[] BuildPalette(long[]? colorMap, int bps)
    {
        int n = 1 << bps;
        if (colorMap == null || colorMap.Length < 3 * n)
            throw new InvalidDataException("TIFF: palette image without a full color map");
        bool eightBit = true;
        for (int i = 0; i < 3 * n; i++)
        {
            if (colorMap[i] > 255)
            {
                eightBit = false;
                break;
            }
        }
        var lut = new byte[256 * 3];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                long v = colorMap[c * n + i] & 0xFFFF;
                lut[i * 3 + c] = eightBit ? (byte)v : (byte)(v >> 8);
            }
        }
        return lut;
    }

    // DECOMPRESSION

    private static int Decompress(int compression, byte[] source, int start, int length, Span<byte> dst, LzwTable? lzw)
    {
        switch (compression)
        {
            case 1:
            {
                int n = System.Math.Min(length, dst.Length);
                source.AsSpan(start, n).CopyTo(dst);
                return n;
            }
            case 5:
                return lzw!.Decode(source.AsSpan(start, length), dst);
            case 32773:
                return PackBits(source.AsSpan(start, length), dst);
            default:
                try
                {
                    using var stream = new ZLibStream(new MemoryStream(source, start, length, false), CompressionMode.Decompress);
                    return stream.ReadAtLeast(dst, dst.Length, throwOnEndOfStream: false);
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException($"TIFF: Deflate data is corrupt ({ex.Message})");
                }
        }
    }

    private static int PackBits(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int i = 0, o = 0;
        while (i < src.Length && o < dst.Length)
        {
            int n = (sbyte)src[i++];
            if (n >= 0)
            {
                int c = System.Math.Min(n + 1, System.Math.Min(src.Length - i, dst.Length - o));
                src.Slice(i, c).CopyTo(dst.Slice(o));
                i += n + 1;
                o += c;
            }
            else if (n != -128)
            {
                if (i >= src.Length)
                    break;
                byte b = src[i++];
                int c = System.Math.Min(1 - n, dst.Length - o);
                dst.Slice(o, c).Fill(b);
                o += c;
            }
        }
        return o;
    }

    // TIFF LZW: 9 to 12 bit codes, 256 clears, 257 ends. Current writers pack codes MSB first and widen
    // one code early; the original 5.0 writers packed LSB first and widened on time. Those streams open
    // with a clear code read the other way round, so the first two bytes say which one this is. -xlinka
    private sealed class LzwTable
    {
        private const int Clear = 256;
        private const int End = 257;
        private readonly short[] _prefix = new short[4096];
        private readonly byte[] _suffix = new byte[4096];
        private readonly ushort[] _length = new ushort[4096];
        private readonly byte[] _first = new byte[4096];

        public LzwTable()
        {
            for (int i = 0; i < 256; i++)
            {
                _prefix[i] = -1;
                _suffix[i] = (byte)i;
                _length[i] = 1;
                _first[i] = (byte)i;
            }
        }

        public int Decode(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            bool lsb = src.Length >= 2 && src[0] == 0 && (src[1] & 1) != 0;
            int early = lsb ? 0 : 1;
            int bits = 9;
            int next = 258;
            int old = -1;
            uint acc = 0;
            int held = 0;
            int ip = 0;
            int op = 0;
            var prefix = _prefix;
            var suffix = _suffix;
            var length = _length;
            var first = _first;

            while (op < dst.Length)
            {
                bool starved = false;
                while (held < bits)
                {
                    if (ip >= src.Length)
                    {
                        starved = true;
                        break;
                    }
                    if (lsb)
                        acc |= (uint)src[ip++] << held;
                    else
                        acc = (acc << 8) | src[ip++];
                    held += 8;
                }
                if (starved)
                    break;

                int code;
                uint mask = (1u << bits) - 1;
                if (lsb)
                {
                    code = (int)(acc & mask);
                    acc >>= bits;
                }
                else
                {
                    code = (int)((acc >> (held - bits)) & mask);
                }
                held -= bits;

                if (code == Clear)
                {
                    bits = 9;
                    next = 258;
                    old = -1;
                    continue;
                }
                if (code == End)
                    break;

                if (old < 0)
                {
                    if (code > 255)
                        break;
                    dst[op++] = (byte)code;
                    old = code;
                    continue;
                }

                int emit;
                if (code < next)
                {
                    emit = code;
                    if (next < 4096)
                        Add(next++, old, first[code]);
                }
                else if (code == next)
                {
                    Add(next++, old, first[old]);
                    emit = code;
                }
                else
                {
                    break;
                }

                int len = length[emit];
                int p = op + len - 1;
                int c = emit;
                if (p < dst.Length)
                {
                    while (c > 255)
                    {
                        dst[p--] = suffix[c];
                        c = prefix[c];
                    }
                    dst[p] = (byte)c;
                    op += len;
                }
                else
                {
                    while (c > 255)
                    {
                        if (p < dst.Length)
                            dst[p] = suffix[c];
                        p--;
                        c = prefix[c];
                    }
                    if (p < dst.Length)
                        dst[p] = (byte)c;
                    op = dst.Length;
                }

                old = code;
                if (next + early >= (1 << bits) && bits < 12)
                    bits++;
            }
            return op;

            void Add(int code, int prefixCode, byte last)
            {
                prefix[code] = (short)prefixCode;
                suffix[code] = last;
                length[code] = (ushort)(length[prefixCode] + 1);
                first[code] = first[prefixCode];
            }
        }
    }

    private static byte ReverseBits(byte b)
    {
        b = (byte)((b >> 4) | (b << 4));
        b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
        return (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
    }

    // SAMPLES

    private static void Swap(Span<byte> buf, int sampleBytes)
    {
        switch (sampleBytes)
        {
            case 2:
            {
                var s = MemoryMarshal.Cast<byte, ushort>(buf);
                BinaryPrimitives.ReverseEndianness(s, s);
                break;
            }
            case 4:
            {
                var s = MemoryMarshal.Cast<byte, uint>(buf);
                BinaryPrimitives.ReverseEndianness(s, s);
                break;
            }
            case 8:
            {
                var s = MemoryMarshal.Cast<byte, ulong>(buf);
                BinaryPrimitives.ReverseEndianness(s, s);
                break;
            }
        }
    }

    // Each row is a running sum of differences, per sample channel, at the sample's own width.
    private static void UndoHorizontal(Span<byte> buf, long rowBytes, long rows, long samples, int stride, int sampleBytes)
    {
        int n = (int)samples;
        for (long r = 0; r < rows; r++)
        {
            var row = buf.Slice((int)(r * rowBytes), (int)rowBytes);
            switch (sampleBytes)
            {
                case 1:
                    for (int i = stride; i < n; i++)
                        row[i] += row[i - stride];
                    break;
                case 2:
                {
                    var s = MemoryMarshal.Cast<byte, ushort>(row);
                    for (int i = stride; i < n; i++)
                        s[i] += s[i - stride];
                    break;
                }
                case 4:
                {
                    var s = MemoryMarshal.Cast<byte, uint>(row);
                    for (int i = stride; i < n; i++)
                        s[i] += s[i - stride];
                    break;
                }
                case 8:
                {
                    var s = MemoryMarshal.Cast<byte, ulong>(row);
                    for (int i = stride; i < n; i++)
                        s[i] += s[i - stride];
                    break;
                }
            }
        }
    }

    // Floating point predictor: bytes are differenced one at a time across the row, and the row holds
    // every sample's most significant byte first, then the next byte plane, whatever the file's order.
    private static void UndoFloat(Span<byte> buf, long rowBytes, long rows, long samples, int stride, int sampleBytes, byte[] tmp)
    {
        int count = (int)samples;
        int n = count * sampleBytes;
        for (long r = 0; r < rows; r++)
        {
            var row = buf.Slice((int)(r * rowBytes), n);
            for (int i = stride; i < n; i++)
                row[i] += row[i - stride];
            row.CopyTo(tmp);
            for (int k = 0; k < count; k++)
            {
                int o = k * sampleBytes;
                for (int b = 0; b < sampleBytes; b++)
                    row[o + b] = tmp[(sampleBytes - b - 1) * count + k];
            }
        }
    }

    private static void Place(byte[] buf, long rowBytes, long rows, long x0, long y0, long chunkW, int w, int h,
        int spc, int[] cmap, int bps, bool isFloat, bool normalize, byte[] rgba)
    {
        int count = (int)System.Math.Min(chunkW, w - x0);
        if (count <= 0)
            return;
        bool rgb8 = bps == 8 && !isFloat && spc == 3 && cmap[0] == 0 && cmap[1] == 1 && cmap[2] == 2;
        bool rgba8 = bps == 8 && !isFloat && spc == 4 && cmap[0] == 0 && cmap[1] == 1 && cmap[2] == 2 && cmap[3] == 3;
        int sampleBytes = bps / 8;

        for (long r = 0; r < rows; r++)
        {
            long y = y0 + r;
            if (y >= h)
                break;
            int src = (int)(r * rowBytes);
            int dst = (int)((y * w + x0) * 4);

            if (rgba8)
            {
                buf.AsSpan(src, count * 4).CopyTo(rgba.AsSpan(dst));
            }
            else if (rgb8)
            {
                for (int x = 0; x < count; x++, src += 3, dst += 4)
                {
                    rgba[dst] = buf[src];
                    rgba[dst + 1] = buf[src + 1];
                    rgba[dst + 2] = buf[src + 2];
                }
            }
            else if (bps < 8)
            {
                int mask = (1 << bps) - 1;
                int c = cmap[0];
                for (int x = 0; x < count; x++, dst += 4)
                {
                    int bit = x * bps;
                    int v = (buf[src + (bit >> 3)] >> (8 - bps - (bit & 7))) & mask;
                    rgba[dst + c] = normalize ? (byte)(v * 255 / mask) : (byte)v;
                }
            }
            else
            {
                for (int x = 0; x < count; x++, dst += 4)
                {
                    for (int s = 0; s < spc; s++, src += sampleBytes)
                    {
                        int c = cmap[s];
                        if (c >= 0)
                            rgba[dst + c] = Sample(buf, src, bps, isFloat);
                    }
                }
            }
        }
    }

    // To 8 bits with rounding. Samples are little-endian here, whatever the file was.
    private static byte Sample(byte[] b, int p, int bps, bool isFloat)
    {
        if (!isFloat)
        {
            switch (bps)
            {
                case 8:
                    return b[p];
                case 16:
                    return (byte)(((b[p] | (b[p + 1] << 8)) * 255 + 32895) >> 16);
                default:
                    ulong v = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
                    return (byte)((v * 255 + 2147483647UL) / 4294967295UL);
            }
        }

        double f = bps switch
        {
            16 => (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2))),
            32 => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(p, 4)),
            _ => BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(p, 8)),
        };
        if (!(f > 0))
            return 0;
        return f >= 1 ? (byte)255 : (byte)(f * 255 + 0.5);
    }

    // Color models into RGB, then alpha: opaque when there is none, un-premultiplied when it was.
    private static void Finish(byte[] rgba, int photometric, byte[]? palette, bool hasAlpha, bool premultiplied)
    {
        int n = rgba.Length;
        switch (photometric)
        {
            case 0:
                for (int i = 0; i < n; i += 4)
                {
                    byte v = (byte)(255 - rgba[i]);
                    rgba[i] = v;
                    rgba[i + 1] = v;
                    rgba[i + 2] = v;
                }
                break;
            case 1:
                for (int i = 0; i < n; i += 4)
                {
                    byte v = rgba[i];
                    rgba[i + 1] = v;
                    rgba[i + 2] = v;
                }
                break;
            case 3:
                for (int i = 0; i < n; i += 4)
                {
                    int k = rgba[i] * 3;
                    rgba[i] = palette![k];
                    rgba[i + 1] = palette[k + 1];
                    rgba[i + 2] = palette[k + 2];
                }
                break;
            case 5:
                for (int i = 0; i < n; i += 4)
                {
                    int k = 255 - rgba[i + 3];
                    rgba[i] = (byte)(((255 - rgba[i]) * k + 127) / 255);
                    rgba[i + 1] = (byte)(((255 - rgba[i + 1]) * k + 127) / 255);
                    rgba[i + 2] = (byte)(((255 - rgba[i + 2]) * k + 127) / 255);
                }
                break;
        }

        if (!hasAlpha)
        {
            for (int i = 3; i < n; i += 4)
                rgba[i] = 255;
        }
        else if (premultiplied)
        {
            for (int i = 0; i < n; i += 4)
            {
                int a = rgba[i + 3];
                if (a == 0 || a == 255)
                    continue;
                rgba[i] = (byte)System.Math.Min(255, (rgba[i] * 255 + a / 2) / a);
                rgba[i + 1] = (byte)System.Math.Min(255, (rgba[i + 1] * 255 + a / 2) / a);
                rgba[i + 2] = (byte)System.Math.Min(255, (rgba[i + 2] * 255 + a / 2) / a);
            }
        }
    }
}
