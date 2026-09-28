// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// The smallest box holding both. An empty box adds nothing.
[Variadic]
[Node("Math/Bounds/Encapsulate Bounds")]
public sealed class EncapsulateBounds : ValueNode
{
    [return: Port("bounds")]
    public static BoundingBox Run(BoundingBox a, BoundingBox b)
    {
        a.Encapsulate(b);
        return a;
    }
}
