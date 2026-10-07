// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Lumora.Core.Assets;

// A PNG decoded a row at a time straight into a BoxReducer, so a picture bigger than the size cap is never
// whole in memory: the inflater, the current and previous row, and the reduced output are all it holds.
// Every colour type and bit depth, tRNS, and Adam7 (whose passes land pixel by pixel). Sixteen-bit samples
// keep their high byte, the same as the full decoder. Null for anything it does not read, so the caller
// can fall back. -xlinka
public static class PngRowDecoder
{
    private static readonly (int X, int Y, int DX, int DY)[] Adam7 =
    {
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    };

    public static byte[]? DecodeReduced(byte[] png, int width, int height, out int sourceWidth, out int sourceHeight)
    {
        sourceWidth = sourceHeight = 0;
        if (png.Length < 33)
            return null;

        int w = 0, h = 0, depth = 0, type = -1, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        int trnsGray = -1, trnsR = -1, trnsG = -1, trnsB = -1;
        var idat = new System.Collections.Generic.List<(int Offset, int Length)>();

        int at = 8;
        while (at + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            var chunk = png.AsSpan(at + 4, 4);
            int data = at + 8;
            if (length < 0 || data + length > png.Length)
                return null;
            if (chunk.SequenceEqual("IHDR"u8))
            {
                w = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(data));
                h = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(data + 4));
                depth = png[data + 8];
                type = png[data + 9];
                interlace = png[data + 12];
            }
            else if (chunk.SequenceEqual("CgBI"u8))
                return null;
            else if (chunk.SequenceEqual("PLTE"u8))
                palette = png.AsSpan(data, length).ToArray();
            else if (chunk.SequenceEqual("tRNS"u8))
            {
                if (type == 3)
                    paletteAlpha = png.AsSpan(data, length).ToArray();
                else if (type == 0 && length >= 2)
                    trnsGray = BinaryPrimitives.ReadUInt16BigEndian(png.AsSpan(data));
                else if (type == 2 && length >= 6)
                {
                    trnsR = BinaryPrimitives.ReadUInt16BigEndian(png.AsSpan(data));
                    trnsG = BinaryPrimitives.ReadUInt16BigEndian(png.AsSpan(data + 2));
                    trnsB = BinaryPrimitives.ReadUInt16BigEndian(png.AsSpan(data + 4));
                }
            }
            else if (chunk.SequenceEqual("IDAT"u8))
                idat.Add((data, length));
            else if (chunk.SequenceEqual("IEND"u8))
                break;
            at = data + length + 4;
        }

        int channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (w <= 0 || h <= 0 || channels == 0 || idat.Count == 0 || interlace > 1)
            return null;
        if (depth is not (1 or 2 or 4 or 8 or 16) || (depth < 8 && type is not (0 or 3)) || (depth == 16 && type == 3))
            return null;
        if (type == 3 && palette == null)
            return null;

        sourceWidth = w;
        sourceHeight = h;
        int bits = channels * depth;
        int filterStride = System.Math.Max(1, bits / 8);
        var reducer = new BoxReducer(w, h, width, height, anyOrder: interlace == 1);
        var context = new Context(type, depth, palette, paletteAlpha, trnsGray, trnsR, trnsG, trnsB);

        using var inflater = new ZLibStream(new ChunkStream(png, idat), CompressionMode.Decompress);
        var rgba = new byte[w * 4];
        if (interlace == 0)
        {
            int rowBytes = (int)(((long)w * bits + 7) / 8);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            for (int y = 0; y < h; y++)
            {
                if (!ReadRow(inflater, current, previous, filterStride))
                    return null;
                context.Expand(current, w, rgba);
                reducer.AddRow(y, rgba.AsSpan(0, w * 4));
                (previous, current) = (current, previous);
            }
        }
        else
        {
            foreach (var pass in Adam7)
            {
                int passWidth = (w - pass.X + pass.DX - 1) / pass.DX;
                int passHeight = (h - pass.Y + pass.DY - 1) / pass.DY;
                if (passWidth <= 0 || passHeight <= 0)
                    continue;
                int rowBytes = (int)(((long)passWidth * bits + 7) / 8);
                var previous = new byte[rowBytes];
                var current = new byte[rowBytes];
                for (int py = 0; py < passHeight; py++)
                {
                    if (!ReadRow(inflater, current, previous, filterStride))
                        return null;
                    context.Expand(current, passWidth, rgba);
                    int y = pass.Y + py * pass.DY;
                    for (int px = 0; px < passWidth; px++)
                    {
                        int i = px * 4;
                        reducer.AddPixel(pass.X + px * pass.DX, y, rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                    }
                    (previous, current) = (current, previous);
                }
            }
        }
        return reducer.Finish();
    }

    private static bool ReadRow(Stream inflater, byte[] row, byte[] previous, int bpp)
    {
        int filter = inflater.ReadByte();
        if (filter < 0)
            return false;
        int read = 0;
        while (read < row.Length)
        {
            int n = inflater.Read(row, read, row.Length - read);
            if (n <= 0)
                return false;
            read += n;
        }
        switch (filter)
        {
            case 0:
                break;
            case 1:
                for (int i = bpp; i < row.Length; i++)
                    row[i] += row[i - bpp];
                break;
            case 2:
                for (int i = 0; i < row.Length; i++)
                    row[i] += previous[i];
                break;
            case 3:
                for (int i = 0; i < row.Length; i++)
                    row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + previous[i]) >> 1);
                break;
            case 4:
                for (int i = 0; i < row.Length; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c;
                    int pa = System.Math.Abs(p - a), pb = System.Math.Abs(p - b), pc = System.Math.Abs(p - c);
                    row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                break;
            default:
                return false;
        }
        return true;
    }

    private sealed class Context
    {
        private readonly int _type;
        private readonly int _depth;
        private readonly byte[]? _palette;
        private readonly byte[]? _paletteAlpha;
        private readonly int _trnsGray, _trnsR, _trnsG, _trnsB;

        public Context(int type, int depth, byte[]? palette, byte[]? paletteAlpha, int trnsGray, int trnsR, int trnsG, int trnsB)
        {
            _type = type;
            _depth = depth;
            _palette = palette;
            _paletteAlpha = paletteAlpha;
            _trnsGray = trnsGray;
            _trnsR = trnsR;
            _trnsG = trnsG;
            _trnsB = trnsB;
        }

        public void Expand(byte[] row, int count, byte[] rgba)
        {
            switch (_type)
            {
                case 0:
                    for (int x = 0; x < count; x++)
                    {
                        int raw = Sample(row, x);
                        byte v = Scale(raw);
                        Put(rgba, x, v, v, v, raw == _trnsGray ? (byte)0 : (byte)255);
                    }
                    break;
                case 3:
                    for (int x = 0; x < count; x++)
                    {
                        int index = Sample(row, x);
                        int p = index * 3;
                        var palette = _palette!;
                        byte r = p + 2 < palette.Length ? palette[p] : (byte)0;
                        byte g = p + 2 < palette.Length ? palette[p + 1] : (byte)0;
                        byte b = p + 2 < palette.Length ? palette[p + 2] : (byte)0;
                        byte a = _paletteAlpha != null && index < _paletteAlpha.Length ? _paletteAlpha[index] : (byte)255;
                        Put(rgba, x, r, g, b, a);
                    }
                    break;
                case 2:
                    if (_depth == 8)
                    {
                        for (int x = 0, i = 0; x < count; x++, i += 3)
                        {
                            bool clear = row[i] == _trnsR && row[i + 1] == _trnsG && row[i + 2] == _trnsB;
                            Put(rgba, x, row[i], row[i + 1], row[i + 2], clear ? (byte)0 : (byte)255);
                        }
                    }
                    else
                    {
                        for (int x = 0, i = 0; x < count; x++, i += 6)
                        {
                            int r = row[i] << 8 | row[i + 1], g = row[i + 2] << 8 | row[i + 3], b = row[i + 4] << 8 | row[i + 5];
                            bool clear = r == _trnsR && g == _trnsG && b == _trnsB;
                            Put(rgba, x, row[i], row[i + 2], row[i + 4], clear ? (byte)0 : (byte)255);
                        }
                    }
                    break;
                case 4:
                    {
                        int step = _depth == 8 ? 2 : 4;
                        for (int x = 0, i = 0; x < count; x++, i += step)
                        {
                            byte v = row[i];
                            byte a = row[i + step / 2];
                            Put(rgba, x, v, v, v, a);
                        }
                    }
                    break;
                case 6:
                    if (_depth == 8)
                        Buffer.BlockCopy(row, 0, rgba, 0, count * 4);
                    else
                    {
                        for (int x = 0, i = 0; x < count; x++, i += 8)
                            Put(rgba, x, row[i], row[i + 2], row[i + 4], row[i + 6]);
                    }
                    break;
            }
        }

        private int Sample(byte[] row, int x)
        {
            switch (_depth)
            {
                case 16:
                    return row[x * 2] << 8 | row[x * 2 + 1];
                case 8:
                    return row[x];
                default:
                    int bit = x * _depth;
                    int shift = 8 - _depth - (bit & 7);
                    return (row[bit >> 3] >> shift) & ((1 << _depth) - 1);
            }
        }

        private byte Scale(int raw) => _depth switch
        {
            16 => (byte)(raw >> 8),
            8 => (byte)raw,
            4 => (byte)(raw * 0x11),
            2 => (byte)(raw * 0x55),
            _ => (byte)(raw * 0xFF),
        };

        private static void Put(byte[] rgba, int x, byte r, byte g, byte b, byte a)
        {
            int o = x * 4;
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = a;
        }
    }

    // The IDAT payloads, back to back, as one stream for the inflater without copying them together.
    private sealed class ChunkStream : Stream
    {
        private readonly byte[] _data;
        private readonly System.Collections.Generic.List<(int Offset, int Length)> _chunks;
        private int _chunk;
        private int _at;

        public ChunkStream(byte[] data, System.Collections.Generic.List<(int Offset, int Length)> chunks)
        {
            _data = data;
            _chunks = chunks;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_chunk < _chunks.Count && _at >= _chunks[_chunk].Length)
            {
                _chunk++;
                _at = 0;
            }
            if (_chunk >= _chunks.Count)
                return 0;
            var (start, length) = _chunks[_chunk];
            int n = System.Math.Min(count, length - _at);
            Buffer.BlockCopy(_data, start + _at, buffer, offset, n);
            _at += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
