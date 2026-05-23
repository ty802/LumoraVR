// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// per-glyph layout data returned by a FontAsset for a codepoint at a given pixel size. - xlinka
public struct GlyphMetrics
{
    // x distance to advance the pen after drawing this glyph - xlinka
    public float Advance;

    // offset from the pen position to the glyph's bottom-left corner - xlinka
    public float2 Offset;

    // glyph quad size in pixels - xlinka
    public float2 Size;
}
