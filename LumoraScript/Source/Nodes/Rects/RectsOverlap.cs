// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Whether the two share some area. Rects that only touch along an edge do not overlap.
[Node("Math/Rects/Overlaps")]
public sealed class RectsOverlap : ValueNode
{
    [return: Port("overlap")]
    public static bool Run(Rect a, Rect b) => a.Overlaps(b);
}
