// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// The rotation that undoes this one.
[Node("Math/Rotations/Inverse")]
public sealed class InverseRotation : ValueNode
{
    [return: Port("inverse")]
    public static floatQ Run(floatQ rotation) => rotation.Inverse;
}
