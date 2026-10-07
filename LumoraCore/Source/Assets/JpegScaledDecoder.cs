// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

// A JPEG decoded straight to a fraction of its size, for a picture bigger than the size cap.
//
// Each 8x8 block is turned back into pixels at 1/2, 1/4 or 1/8 of its size from its lowest frequencies
// alone, weighted by what averaging the full-size pixels would have done to each one, so a block costs a
// few coefficients and a few pixels instead of sixty-four of each. Only those low coefficients are kept
// while the scans are read, plus one bit a coefficient for a progressive file's refinement scans, and the
// scaled rows go through a BoxReducer down to the exact size asked for: nothing the size of the source is
// ever allocated. Baseline and progressive Huffman, 8-bit, one or three components, any sampling factors,
// restart markers. Null for anything else (arithmetic coding, 12-bit, CMYK) so the caller can fall
// back. -xlinka
public static class JpegScaledDecoder
{
    private static readonly int[] ZigZag =
    {
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21,
        28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61,
        54, 47, 55, 62, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    };

    private sealed class Huffman
    {
        public readonly byte[] Values = new byte[256];
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValPtr = new int[17];
        public readonly int[] MinCode = new int[17];
        // 9-bit lookahead: length in the high byte, value in the low byte, 0 when the code is longer.
        public readonly ushort[] Fast = new ushort[512];

        public void Build(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            values.CopyTo(Values);
            int code = 0, k = 0;
            for (int length = 1; length <= 16; length++)
            {
                ValPtr[length] = k;
                MinCode[length] = code;
                code += counts[length - 1];
                k += counts[length - 1];
                MaxCode[length] = counts[length - 1] > 0 ? code - 1 : -1;
                code <<= 1;
            }
            MaxCode[17] = int.MaxValue;
            Array.Clear(Fast);
            code = 0;
            k = 0;
            for (int length = 1; length <= 9; length++)
            {
                for (int i = 0; i < counts[length - 1]; i++, k++, code++)
                {
                    int shift = 9 - length;
                    for (int fill = 0; fill < 1 << shift; fill++)
                        Fast[(code << shift) | fill] = (ushort)(length << 8 | values[k]);
                }
                code <<= 1;
            }
        }
    }

    private sealed class Component
    {
        public int Id, H, V, Tq;
        public int BlocksX, BlocksY;
        public int ScanBlocksX, ScanBlocksY;
        public short[] Coefficients = Array.Empty<short>();
        public ulong[] Nonzero = Array.Empty<ulong>();
        public int DcPredictor;
        public int DcTable, AcTable;
    }

    private sealed class Reader
    {
        private readonly byte[] _data;
        public int Position;
        private uint _bits;
        private int _count;
        public bool HitMarker;

        public Reader(byte[] data, int position)
        {
            _data = data;
            Position = position;
        }

        private void Fill()
        {
            while (_count <= 24)
            {
                int b = 0;
                if (!HitMarker && Position < _data.Length)
                {
                    b = _data[Position];
                    if (b == 0xFF)
                    {
                        int next = Position + 1 < _data.Length ? _data[Position + 1] : 0xD9;
                        if (next == 0x00)
                            Position += 2;
                        else
                        {
                            HitMarker = true;
                            b = 0;
                        }
                    }
                    else
                        Position++;
                }
                _bits |= (uint)b << (24 - _count);
                _count += 8;
            }
        }

        public int Bits(int n)
        {
            if (n == 0)
                return 0;
            if (_count < n)
                Fill();
            int value = (int)(_bits >> (32 - n));
            _bits <<= n;
            _count -= n;
            return value;
        }

        public int Bit() => Bits(1);

        public int Decode(Huffman table)
        {
            if (_count < 16)
                Fill();
            int look = (int)(_bits >> 23);
            int fast = table.Fast[look];
            if (fast != 0)
            {
                int length = fast >> 8;
                _bits <<= length;
                _count -= length;
                return fast & 0xFF;
            }
            int code = (int)(_bits >> 22);
            int len = 10;
            while (len <= 16 && code > table.MaxCode[len])
            {
                len++;
                code = (int)(_bits >> (32 - len));
            }
            if (len > 16)
                throw new InvalidOperationException("bad Huffman code");
            _bits <<= len;
            _count -= len;
            return table.Values[table.ValPtr[len] + code - table.MinCode[len]];
        }

        public static int Extend(int value, int bits) => bits == 0 ? 0 : value < 1 << (bits - 1) ? value - (1 << bits) + 1 : value;

        // Drop what is buffered and step over the restart marker the scan stopped at.
        public void Restart()
        {
            _bits = 0;
            _count = 0;
            HitMarker = false;
            while (Position + 1 < _data.Length && !(_data[Position] == 0xFF && _data[Position + 1] >= 0xD0 && _data[Position + 1] <= 0xD7))
                Position++;
            if (Position + 1 < _data.Length)
                Position += 2;
        }
    }

    public static byte[]? DecodeReduced(byte[] jpeg, int width, int height, out int sourceWidth, out int sourceHeight)
    {
        sourceWidth = sourceHeight = 0;
        try
        {
            return Decode(jpeg, width, height, out sourceWidth, out sourceHeight);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? Decode(byte[] data, int targetWidth, int targetHeight, out int sourceWidth, out int sourceHeight)
    {
        sourceWidth = sourceHeight = 0;
        var quant = new int[4][];
        var dc = new Huffman[4];
        var ac = new Huffman[4];
        Component[] components = Array.Empty<Component>();
        bool progressive = false, frame = false;
        int restartInterval = 0;
        int adobeTransform = -1;
        int w = 0, h = 0, hMax = 1, vMax = 1, mcusX = 0, mcusY = 0;
        int k = 8, scale = 1;
        int eobRun = 0;

        int at = 2;
        while (at + 4 <= data.Length)
        {
            if (data[at] != 0xFF)
            {
                at++;
                continue;
            }
            int marker = data[at + 1];
            if (marker == 0xFF || marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                at += marker == 0xFF ? 1 : 2;
                continue;
            }
            if (marker == 0xD9)
                break;
            int length = data[at + 2] << 8 | data[at + 3];
            int seg = at + 4;
            int end = at + 2 + length;
            switch (marker)
            {
                case 0xDB:
                    for (int p = seg; p < end;)
                    {
                        int pq = data[p] >> 4, tq = data[p] & 15;
                        p++;
                        var table = new int[64];
                        for (int i = 0; i < 64; i++)
                        {
                            table[ZigZag[i]] = pq == 0 ? data[p] : data[p] << 8 | data[p + 1];
                            p += pq == 0 ? 1 : 2;
                        }
                        quant[tq & 3] = table;
                    }
                    break;
                case 0xC4:
                    for (int p = seg; p < end;)
                    {
                        int tc = data[p] >> 4, th = data[p] & 3;
                        var counts = data.AsSpan(p + 1, 16);
                        int total = 0;
                        foreach (var c in counts)
                            total += c;
                        var table = new Huffman();
                        table.Build(counts, data.AsSpan(p + 17, total));
                        if (tc == 0) dc[th] = table;
                        else ac[th] = table;
                        p += 17 + total;
                    }
                    break;
                case 0xDD:
                    restartInterval = data[seg] << 8 | data[seg + 1];
                    break;
                case 0xEE:
                    if (length >= 12 && data.AsSpan(seg, 5).SequenceEqual("Adobe"u8))
                        adobeTransform = data[seg + 11];
                    break;
                case 0xC0:
                case 0xC1:
                case 0xC2:
                    {
                        if (data[seg] != 8)
                            return null;
                        progressive = marker == 0xC2;
                        h = data[seg + 1] << 8 | data[seg + 2];
                        w = data[seg + 3] << 8 | data[seg + 4];
                        int count = data[seg + 5];
                        if (w <= 0 || h <= 0 || (count != 1 && count != 3))
                            return null;
                        components = new Component[count];
                        for (int i = 0; i < count; i++)
                        {
                            int c = seg + 6 + i * 3;
                            components[i] = new Component { Id = data[c], H = System.Math.Max(1, data[c + 1] >> 4), V = System.Math.Max(1, data[c + 1] & 15), Tq = data[c + 2] & 3 };
                            hMax = System.Math.Max(hMax, components[i].H);
                            vMax = System.Math.Max(vMax, components[i].V);
                        }
                        mcusX = (w + 8 * hMax - 1) / (8 * hMax);
                        mcusY = (h + 8 * vMax - 1) / (8 * vMax);

                        // The smallest decode that is still at least the size asked for.
                        foreach (int d in new[] { 8, 4, 2, 1 })
                        {
                            if ((w + d - 1) / d >= targetWidth && (h + d - 1) / d >= targetHeight)
                            {
                                scale = d;
                                break;
                            }
                        }
                        k = 8 / scale;
                        foreach (var c in components)
                        {
                            c.BlocksX = mcusX * c.H;
                            c.BlocksY = mcusY * c.V;
                            c.ScanBlocksX = ((w * c.H + hMax - 1) / hMax + 7) / 8;
                            c.ScanBlocksY = ((h * c.V + vMax - 1) / vMax + 7) / 8;
                            long blocks = (long)c.BlocksX * c.BlocksY;
                            c.Coefficients = new short[blocks * k * k];
                            if (progressive)
                                c.Nonzero = new ulong[blocks];
                        }
                        frame = true;
                        sourceWidth = w;
                        sourceHeight = h;
                    }
                    break;
                case 0xDA:
                    {
                        if (!frame)
                            return null;
                        int ns = data[seg];
                        var scan = new Component[ns];
                        for (int i = 0; i < ns; i++)
                        {
                            int id = data[seg + 1 + i * 2];
                            int tables = data[seg + 2 + i * 2];
                            var c = Array.Find(components, x => x.Id == id) ?? throw new InvalidOperationException("scan names no component");
                            c.DcTable = tables >> 4;
                            c.AcTable = tables & 15;
                            scan[i] = c;
                        }
                        int p = seg + 1 + ns * 2;
                        int ss = data[p], se = data[p + 1], ah = data[p + 2] >> 4, al = data[p + 2] & 15;
                        var reader = new Reader(data, end);
                        foreach (var c in components)
                            c.DcPredictor = 0;
                        eobRun = 0;
                        DecodeScan(reader, scan, progressive, ss, se, ah, al, dc, ac, k, restartInterval, mcusX, mcusY, ref eobRun);
                        at = reader.Position;
                        // Find the marker after the entropy-coded data.
                        while (at + 1 < data.Length && !(data[at] == 0xFF && data[at + 1] != 0 && (data[at + 1] < 0xD0 || data[at + 1] > 0xD7)))
                            at++;
                        continue;
                    }
                case >= 0xC3 and <= 0xCF when marker is not (0xC4 or 0xC8 or 0xCC):
                    return null;
            }
            at = end;
        }

        if (!frame)
            return null;
        foreach (var c in components)
        {
            if (quant[c.Tq] == null)
                return null;
        }

        bool rgb = components.Length == 3 && (adobeTransform == 0 || (adobeTransform < 0 && components[0].Id == 'R' && components[1].Id == 'G' && components[2].Id == 'B'));
        return Render(components, quant, w, h, hMax, vMax, mcusY, k, scale, rgb, targetWidth, targetHeight);
    }

    private static void DecodeScan(Reader reader, Component[] scan, bool progressive, int ss, int se, int ah, int al,
        Huffman[] dc, Huffman[] ac, int k, int restartInterval, int mcusX, int mcusY, ref int eobRun)
    {
        var block = new int[64];
        int units = 0;
        if (scan.Length == 1)
        {
            var c = scan[0];
            for (int by = 0; by < c.ScanBlocksY; by++)
            {
                for (int bx = 0; bx < c.ScanBlocksX; bx++)
                {
                    if (restartInterval > 0 && units > 0 && units % restartInterval == 0)
                    {
                        reader.Restart();
                        foreach (var x in scan)
                            x.DcPredictor = 0;
                        eobRun = 0;
                    }
                    DecodeBlock(reader, c, by * c.BlocksX + bx, progressive, ss, se, ah, al, dc, ac, k, block, ref eobRun);
                    units++;
                }
            }
            return;
        }

        for (int my = 0; my < mcusY; my++)
        {
            for (int mx = 0; mx < mcusX; mx++)
            {
                if (restartInterval > 0 && units > 0 && units % restartInterval == 0)
                {
                    reader.Restart();
                    foreach (var x in scan)
                        x.DcPredictor = 0;
                    eobRun = 0;
                }
                foreach (var c in scan)
                {
                    for (int v = 0; v < c.V; v++)
                    {
                        for (int hh = 0; hh < c.H; hh++)
                        {
                            int index = (my * c.V + v) * c.BlocksX + mx * c.H + hh;
                            DecodeBlock(reader, c, index, progressive, ss, se, ah, al, dc, ac, k, block, ref eobRun);
                        }
                    }
                }
                units++;
            }
        }
    }

    // Natural position -> slot in the kept k x k corner, or -1 when the coefficient is not kept.
    private static int Kept(int natural, int k)
    {
        int row = natural >> 3, column = natural & 7;
        return row < k && column < k ? row * k + column : -1;
    }

    private static void DecodeBlock(Reader reader, Component c, int index, bool progressive, int ss, int se, int ah, int al,
        Huffman[] dc, Huffman[] ac, int k, int[] block, ref int eobRun)
    {
        long baseSlot = (long)index * k * k;
        var coefficients = c.Coefficients;

        if (!progressive)
        {
            int t = reader.Decode(dc[c.DcTable]);
            int diff = t == 0 ? 0 : Reader.Extend(reader.Bits(t), t);
            c.DcPredictor += diff;
            coefficients[baseSlot] = (short)c.DcPredictor;
            var table = ac[c.AcTable];
            for (int z = 1; z < 64;)
            {
                int rs = reader.Decode(table);
                int r = rs >> 4, s = rs & 15;
                if (s == 0)
                {
                    if (r != 15)
                        break;
                    z += 16;
                    continue;
                }
                z += r;
                if (z > 63)
                    break;
                int value = Reader.Extend(reader.Bits(s), s);
                int slot = Kept(ZigZag[z], k);
                if (slot >= 0)
                    coefficients[baseSlot + slot] = (short)value;
                z++;
            }
            return;
        }

        ref ulong nonzero = ref c.Nonzero[index];
        if (ss == 0)
        {
            if (ah == 0)
            {
                int t = reader.Decode(dc[c.DcTable]);
                int diff = t == 0 ? 0 : Reader.Extend(reader.Bits(t), t);
                c.DcPredictor += diff;
                int value = c.DcPredictor * (1 << al);
                coefficients[baseSlot] = (short)value;
                if (value != 0)
                    nonzero |= 1UL;
            }
            else if (reader.Bit() != 0)
            {
                coefficients[baseSlot] |= (short)(1 << al);
                nonzero |= 1UL;
            }
            return;
        }

        var acTable = ac[c.AcTable];
        if (ah == 0)
        {
            if (eobRun > 0)
            {
                eobRun--;
                return;
            }
            for (int z = ss; z <= se;)
            {
                int rs = reader.Decode(acTable);
                int r = rs >> 4, s = rs & 15;
                if (s == 0)
                {
                    if (r < 15)
                    {
                        eobRun = (1 << r) - 1;
                        if (r > 0)
                            eobRun += reader.Bits(r);
                        break;
                    }
                    z += 16;
                    continue;
                }
                z += r;
                if (z > 63)
                    break;
                int value = Reader.Extend(reader.Bits(s), s) * (1 << al);
                int natural = ZigZag[z];
                nonzero |= 1UL << natural;
                int slot = Kept(natural, k);
                if (slot >= 0)
                    coefficients[baseSlot + slot] = (short)value;
                z++;
            }
            return;
        }

        // Refinement: a correction bit for every coefficient already nonzero, new ones placed in the zeros.
        int p1 = 1 << al, m1 = -1 << al;
        int kz = ss;
        if (eobRun <= 0)
        {
            for (; kz <= se; kz++)
            {
                int rs = reader.Decode(acTable);
                int r = rs >> 4, s = rs & 15;
                int newValue = 0;
                if (s != 0)
                    newValue = reader.Bit() != 0 ? p1 : m1;
                else if (r != 15)
                {
                    eobRun = 1 << r;
                    if (r > 0)
                        eobRun += reader.Bits(r);
                    break;
                }
                while (kz <= se)
                {
                    int natural = ZigZag[kz];
                    if ((nonzero & (1UL << natural)) != 0)
                    {
                        if (reader.Bit() != 0)
                            Correct(coefficients, baseSlot, Kept(natural, k), p1, m1);
                    }
                    else
                    {
                        if (--r < 0)
                            break;
                    }
                    kz++;
                }
                if (newValue != 0 && kz <= se)
                {
                    int natural = ZigZag[kz];
                    nonzero |= 1UL << natural;
                    int slot = Kept(natural, k);
                    if (slot >= 0)
                        coefficients[baseSlot + slot] = (short)newValue;
                }
            }
        }
        if (eobRun > 0)
        {
            for (; kz <= se; kz++)
            {
                int natural = ZigZag[kz];
                if ((nonzero & (1UL << natural)) != 0 && reader.Bit() != 0)
                    Correct(coefficients, baseSlot, Kept(natural, k), p1, m1);
            }
            eobRun--;
        }
    }

    private static void Correct(short[] coefficients, long baseSlot, int slot, int p1, int m1)
    {
        if (slot < 0)
            return;
        int value = coefficients[baseSlot + slot];
        if ((value & p1) == 0)
            coefficients[baseSlot + slot] = (short)(value >= 0 ? value + p1 : value + m1);
    }

    // basis[x * k + u]: what frequency u contributes to output sample x of a block scaled to k samples,
    // weighted by how averaging 8 / k full-size samples would have passed it.
    private static float[] Basis(int k)
    {
        var basis = new float[k * k];
        int m = 8 / k;
        for (int x = 0; x < k; x++)
        {
            for (int u = 0; u < k; u++)
            {
                double c = u == 0 ? 1.0 / System.Math.Sqrt(2.0) : 1.0;
                double box = u == 0 ? 1.0 : System.Math.Sin(m * u * System.Math.PI / 16) / (m * System.Math.Sin(u * System.Math.PI / 16));
                basis[x * k + u] = (float)(c / 2 * box * System.Math.Cos((2 * x + 1) * u * System.Math.PI / (2 * k)));
            }
        }
        return basis;
    }

    private static byte[] Render(Component[] components, int[][] quant, int w, int h, int hMax, int vMax, int mcusY, int k, int scale,
        bool rgb, int targetWidth, int targetHeight)
    {
        int scaledWidth = (w + scale - 1) / scale;
        int scaledHeight = (h + scale - 1) / scale;
        var reducer = new BoxReducer(scaledWidth, scaledHeight, targetWidth, targetHeight);
        var basis = Basis(k);
        var temp = new float[k * k];
        var dequant = new float[k * k];

        // One MCU row of each component, at scaled size.
        var strips = new byte[components.Length][];
        var stripWidth = new int[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            stripWidth[i] = components[i].BlocksX * k;
            strips[i] = new byte[stripWidth[i] * components[i].V * k];
        }
        var row = new byte[scaledWidth * 4];
        int mcuRows = vMax * k;

        for (int my = 0; my < mcusY; my++)
        {
            for (int ci = 0; ci < components.Length; ci++)
            {
                var c = components[ci];
                var q = quant[c.Tq];
                for (int i = 0; i < k * k; i++)
                    dequant[i] = q[(i / k) * 8 + i % k];
                for (int v = 0; v < c.V; v++)
                {
                    int by = my * c.V + v;
                    for (int bx = 0; bx < c.BlocksX; bx++)
                    {
                        long slot = ((long)by * c.BlocksX + bx) * k * k;
                        Idct(c.Coefficients, slot, dequant, basis, temp, k, strips[ci], stripWidth[ci], bx * k, v * k);
                    }
                }
            }

            for (int ry = 0; ry < mcuRows; ry++)
            {
                int y = my * mcuRows + ry;
                if (y >= scaledHeight)
                    break;
                for (int x = 0; x < scaledWidth; x++)
                {
                    byte Sample(int ci)
                    {
                        var c = components[ci];
                        int sx = x * c.H / hMax;
                        int sy = ry * c.V / vMax;
                        return strips[ci][sy * stripWidth[ci] + sx];
                    }
                    int o = x * 4;
                    if (components.Length == 1)
                    {
                        byte g = Sample(0);
                        row[o] = row[o + 1] = row[o + 2] = g;
                    }
                    else if (rgb)
                    {
                        row[o] = Sample(0);
                        row[o + 1] = Sample(1);
                        row[o + 2] = Sample(2);
                    }
                    else
                    {
                        int yy = Sample(0) << 16;
                        int cb = Sample(1) - 128, cr = Sample(2) - 128;
                        row[o] = Clamp((yy + 91881 * cr + 32768) >> 16);
                        row[o + 1] = Clamp((yy - 22554 * cb - 46802 * cr + 32768) >> 16);
                        row[o + 2] = Clamp((yy + 116130 * cb + 32768) >> 16);
                    }
                    row[o + 3] = 255;
                }
                reducer.AddRow(y, row);
            }
        }
        return reducer.Finish();
    }

    private static void Idct(short[] coefficients, long slot, float[] dequant, float[] basis, float[] temp, int k, byte[] strip, int stride, int x0, int y0)
    {
        // Rows first: temp[v * k + x] = sum over u of F(v, u) * basis[x][u].
        for (int v = 0; v < k; v++)
        {
            for (int x = 0; x < k; x++)
            {
                float sum = 0;
                for (int u = 0; u < k; u++)
                {
                    int i = v * k + u;
                    sum += coefficients[slot + i] * dequant[i] * basis[x * k + u];
                }
                temp[v * k + x] = sum;
            }
        }
        for (int y = 0; y < k; y++)
        {
            for (int x = 0; x < k; x++)
            {
                float sum = 0;
                for (int v = 0; v < k; v++)
                    sum += temp[v * k + x] * basis[y * k + v];
                strip[(y0 + y) * stride + x0 + x] = Clamp((int)MathF.Round(sum + 128f));
            }
        }
    }

    private static byte Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
