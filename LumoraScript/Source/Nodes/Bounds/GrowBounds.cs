// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Every face pushed out by amount, so the size grows by twice it; a negative amount pulls the faces in.
// An empty box stays empty.
[Node("Math/Bounds/Grow")]
public sealed class GrowBounds : ValueNode
{
    [return: Port("grown")]
    public static BoundingBox Run(BoundingBox bounds, float3 amount) => new(bounds.Min - amount, bounds.Max + amount);

    [return: Port("grown")]
    public static BoundingBox Run(BoundingBox bounds, float amount)
        => new(bounds.Min - new float3(amount, amount, amount), bounds.Max + new float3(amount, amount, amount));
}
