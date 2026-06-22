// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using K4os.Compression.LZ4;

namespace Lumora.Core.External.Compression.lz4;

/// <summary>
/// LZ4 block-format codec. Pure-managed (no native shared library), so it doesn't reproduce
/// the native-dependency headaches some of our other interop has. Uses the fastest level,
/// which is the right trade for the per-frame sync delta path - we want low CPU, not the
/// last few percent of ratio. -xlinka
/// </summary>
public sealed class Lz4BlockCompressable : ICompressable
{
    public int MaxCompressedLength(int sourceLength) => LZ4Codec.MaximumOutputSize(sourceLength);

    public int Compress(ReadOnlySpan<byte> source, Span<byte> target)
    {
        // Returns bytes written, or a non-positive value when it couldn't fit / wasn't worth
        // it. Normalize that to 0 so the framing layer falls back to sending raw.
        int written = LZ4Codec.Encode(source, target, LZ4Level.L00_FAST);
        return written > 0 ? written : 0;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> target, int expectedLength)
    {
        return LZ4Codec.Decode(source, target);
    }
}
