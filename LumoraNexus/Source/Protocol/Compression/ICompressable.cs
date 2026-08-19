// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Nexus.Protocol.Compression;

// A swappable block-compression codec. The methods are span-based so the per-frame
// network hot path doesn't churn the GC, and implementations are expected to be stateless
// and thread-safe (one instance shared across the whole session). Swap the concrete codec
// behind this interface without touching the wire framing. -xlinka
public interface ICompressable
{
    // worst-case compressed size, used to size a scratch buffer before compressing
    int MaxCompressedLength(int sourceLength);

    // returns bytes written, or 0 if it wouldn't fit in target (caller then sends raw)
    int Compress(ReadOnlySpan<byte> source, Span<byte> target);

    // target must already be sized to expectedLength
    int Decompress(ReadOnlySpan<byte> source, Span<byte> target, int expectedLength);
}
