// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.External.Compression;

/// <summary>
/// A swappable block-compression codec. The methods are span-based so the per-frame
/// network hot path doesn't churn the GC, and implementations are expected to be stateless
/// and thread-safe (one instance shared across the whole session). Swap the concrete codec
/// behind this interface without touching the wire framing. -xlinka
/// </summary>
public interface ICompressable
{
    /// <summary>
    /// Worst-case compressed size for a source of <paramref name="sourceLength"/> bytes,
    /// used to size a scratch buffer before compressing.
    /// </summary>
    int MaxCompressedLength(int sourceLength);

    /// <summary>
    /// Compress <paramref name="source"/> into <paramref name="target"/>. Returns the number
    /// of bytes written, or 0 if the data couldn't be compressed into the target (e.g. it
    /// would expand, or the target was too small) - the caller then sends the data raw.
    /// </summary>
    int Compress(ReadOnlySpan<byte> source, Span<byte> target);

    /// <summary>
    /// Decompress <paramref name="source"/> into <paramref name="target"/>, which the caller
    /// has sized to <paramref name="expectedLength"/>. Returns the number of bytes written.
    /// </summary>
    int Decompress(ReadOnlySpan<byte> source, Span<byte> target, int expectedLength);
}
