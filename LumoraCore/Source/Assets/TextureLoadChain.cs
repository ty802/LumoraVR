// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Assets;

// The ordered set of variants a texture request is allowed to pass through on its way to the one it
// actually asked for.
//
// A request names exactly one variant, and waiting for that one variant is why a world full of
// 2048px textures shows nothing until every last byte has landed and decoded. The chain turns that
// single wait into a sequence of acceptable answers: something small and already on disk goes up
// first, the exact one replaces it when it arrives. Every rung is a real generated blob at a real
// derived address, so no rung needs a manifest, a negotiation, or a round trip to find out it does
// not exist.
//
// Two rules keep this honest. A rung is never LARGER than what was asked for, because handing back
// more pixels than the requester budgeted for is not an optimization, it is ignoring the request.
// And no rung may cross the quality ceiling, because a machine capped at 512 asked to be capped at
// 512 and a "temporary" 2048 upload still allocates 2048 worth of VRAM. -xlinka
public static class TextureLoadChain
{
    // Where a progressive load is allowed to stop on the way up. Two rungs, not every bucket: each
    // one costs a gather plus a full GPU upload, and four of those on the way to a 2048 burn more
    // total time than they save in time-to-first-pixel. 128 is the "something is there" rung, 512 is
    // already close enough to read text off. Ascending, because that is the load order.
    public static readonly int[] LadderSizes = { 128, 512 };

    // The pixel size a variant actually resolves to for a source of this longest edge. The original
    // is the source itself; a bucket never upscales, so it clamps against the source.
    public static int Rank(TextureVariantId id, int sourceEdge)
    {
        if (sourceEdge <= 0)
            return id.IsOriginal ? 0 : id.MaxSize;
        return id.IsOriginal ? sourceEdge : System.Math.Min(id.MaxSize, sourceEdge);
    }

    // Smallest first, exact last. The last entry is always the variant the descriptor asked for,
    // even when that is the original, so a caller can walk the whole list and finish on the right
    // one without a special case.
    public static List<TextureVariantId> Build(int sourceWidth, int sourceHeight, TextureVariantDescriptor descriptor)
    {
        var target = descriptor.ResolveVariant(sourceWidth, sourceHeight);
        var chain = new List<TextureVariantId>(LadderSizes.Length + 1);

        int sourceEdge = System.Math.Max(sourceWidth, sourceHeight);
        int targetEdge = Rank(target, sourceEdge);
        int ceiling = Ceiling(descriptor);

        foreach (int size in LadderSizes)
        {
            // >= targetEdge would be a rung at or above the answer, which is either pointless or a
            // rule break. >= sourceEdge is a bucket nobody generates: the generator skips buckets
            // that cannot shrink the source, so asking for one is a guaranteed miss.
            if (size >= targetEdge || size > ceiling || size >= sourceEdge)
                continue;
            chain.Add(new TextureVariantId(size, target.Mipmaps, target.Compression));
        }

        chain.Add(target);
        return chain;
    }

    // 0 means uncapped. Kept as its own function because "no cap" is spelled with a zero in the
    // descriptor and with an infinity in every comparison, and mixing those up silently lets a
    // capped world load an uncapped rung.
    public static int Ceiling(TextureVariantDescriptor descriptor) =>
        descriptor.MaxSize > 0 ? descriptor.MaxSize : int.MaxValue;

    // Pick the best stand-in for target out of what is actually available. Closest below wins;
    // anything larger than the target, over the ceiling, or built with different mip/compression
    // intent is not a candidate at all, since those change what the GPU ends up holding rather than
    // just how much of it.
    public static bool TryPickBest(
        IEnumerable<TextureVariantId> available,
        TextureVariantId target,
        int sourceEdge,
        int ceiling,
        out TextureVariantId best)
    {
        best = default;
        if (available == null)
            return false;

        int targetEdge = Rank(target, sourceEdge);
        int bestEdge = 0;
        bool found = false;

        foreach (var candidate in available)
        {
            if (candidate.Mipmaps != target.Mipmaps || candidate.Compression != target.Compression)
                continue;

            int edge = Rank(candidate, sourceEdge);
            if (edge <= 0 || edge > targetEdge)
                continue;
            if (ceiling > 0 && edge > ceiling)
                continue;

            if (!found || edge > bestEdge)
            {
                best = candidate;
                bestEdge = edge;
                found = true;
            }
        }

        return found;
    }
}
