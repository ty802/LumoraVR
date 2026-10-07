// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

// Shrinks a picture whose rows arrive one at a time, so the full-size picture is never held. Every source
// pixel lands in exactly one output pixel and each output pixel is the plain average of the ones that
// landed in it, alpha straight like every other resample in the engine. Rows that arrive in order, up or
// down, need one output row of sums; rows in any order (an interlaced PNG) need sums for the whole
// output. Either way the most it holds is a few times the OUTPUT, never the source. -xlinka
public sealed class BoxReducer
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly int[] _columnOf;
    private readonly int[] _columnCount;
    private readonly int[] _rowCount;
    private readonly uint[] _sums;
    private readonly bool _anyOrder;
    private int _openRow = -1;

    public int Width { get; }
    public int Height { get; }
    public byte[] Result { get; }

    public BoxReducer(int sourceWidth, int sourceHeight, int width, int height, bool anyOrder = false)
    {
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        Width = width;
        Height = height;
        _anyOrder = anyOrder;
        _columnOf = new int[sourceWidth];
        _columnCount = new int[width];
        for (int x = 0; x < sourceWidth; x++)
        {
            int column = (int)((long)x * width / sourceWidth);
            _columnOf[x] = column;
            _columnCount[column]++;
        }
        _rowCount = new int[height];
        for (int y = 0; y < sourceHeight; y++)
            _rowCount[RowOf(y)]++;
        _sums = new uint[(anyOrder ? (long)width * height : width) * 4];
        Result = new byte[(long)width * height * 4];
    }

    // The output size the size cap gives a source, matching the rung of that size.
    public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight, int cap)
        => new TextureVariantId(cap, true, TextureCompressionKind.None).ResolveSize(sourceWidth, sourceHeight);

    public int RowOf(int sourceRow) => (int)((long)sourceRow * Height / _sourceHeight);

    // One source row of RGBA8.
    public void AddRow(int sourceRow, ReadOnlySpan<byte> rgba)
    {
        int row = RowOf(sourceRow);
        int baseIndex = 0;
        if (_anyOrder)
            baseIndex = row * Width * 4;
        else if (row != _openRow)
        {
            if (_openRow >= 0)
                Flush(_openRow);
            _openRow = row;
        }
        var sums = _sums;
        var columns = _columnOf;
        int count = System.Math.Min(_sourceWidth, rgba.Length / 4);
        for (int x = 0, i = 0; x < count; x++, i += 4)
        {
            int o = baseIndex + columns[x] * 4;
            sums[o] += rgba[i];
            sums[o + 1] += rgba[i + 1];
            sums[o + 2] += rgba[i + 2];
            sums[o + 3] += rgba[i + 3];
        }
    }

    // One pixel, for sources that hand pixels over out of row order. Needs anyOrder.
    public void AddPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        int o = (RowOf(y) * Width + _columnOf[x]) * 4;
        _sums[o] += r;
        _sums[o + 1] += g;
        _sums[o + 2] += b;
        _sums[o + 3] += a;
    }

    public byte[] Finish()
    {
        if (_anyOrder)
        {
            for (int row = 0; row < Height; row++)
                Divide(row, row * Width * 4);
        }
        else if (_openRow >= 0)
        {
            Flush(_openRow);
            _openRow = -1;
        }
        return Result;
    }

    private void Flush(int row)
    {
        Divide(row, 0);
        Array.Clear(_sums, 0, Width * 4);
    }

    private void Divide(int row, int sumBase)
    {
        int rows = System.Math.Max(1, _rowCount[row]);
        var result = Result;
        int outBase = row * Width * 4;
        for (int x = 0; x < Width; x++)
        {
            uint n = (uint)(System.Math.Max(1, _columnCount[x]) * rows);
            uint half = n >> 1;
            int s = sumBase + x * 4;
            int o = outBase + x * 4;
            result[o] = (byte)((_sums[s] + half) / n);
            result[o + 1] = (byte)((_sums[s + 1] + half) / n);
            result[o + 2] = (byte)((_sums[s + 2] + half) / n);
            result[o + 3] = (byte)((_sums[s + 3] + half) / n);
        }
    }

    // The same reduction of a picture already in memory, for a source only the platform can decode.
    public static byte[] Reduce(byte[] rgba, int sourceWidth, int sourceHeight, int width, int height)
    {
        var reducer = new BoxReducer(sourceWidth, sourceHeight, width, height);
        int stride = sourceWidth * 4;
        for (int y = 0; y < sourceHeight; y++)
            reducer.AddRow(y, rgba.AsSpan(y * stride, stride));
        return reducer.Finish();
    }
}
