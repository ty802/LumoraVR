// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Grown just enough to hold the point. An empty box becomes the point itself.
[Node("Math/Bounds/Grow To Point")]
public sealed class GrowBoundsToPoint : ValueNode
{
    [return: Port("grown")]
    public static BoundingBox Run(BoundingBox bounds, float3 point)
    {
        bounds.Encapsulate(point);
        return bounds;
    }
}
