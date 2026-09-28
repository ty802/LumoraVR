// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// The box around this one after the matrix moves, turns and scales it: exactly the box around its eight
// corners carried through. The matrix is read as a transform; a projective bottom row is not used. An
// empty box stays empty.
[Node("Math/Bounds/Transform Bounds")]
public sealed class TransformBounds : ValueNode
{
    [return: Port("transformed")]
    public static BoundingBox Run(BoundingBox bounds, float4x4 matrix) => bounds.Transformed(matrix);
}
