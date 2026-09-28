// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// A transform matrix: scaled, then turned, then moved, the way a slot is placed in its parent. The
// rotation is normalized first, and a zero rotation turns nothing.
[Node("Math/Matrices/Compose TRS")]
public sealed class ComposeTRS : ValueNode
{
    [return: Port("matrix")]
    public static float4x4 Run(float3 position, floatQ rotation, float3 scale) => float4x4.TRS(position, rotation.Normalized, scale);
}
