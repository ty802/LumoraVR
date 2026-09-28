// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// a times b: the matrix that applies b first and then a, the way a parent's matrix times a child's
// places the child. With more inputs the last one is applied first.
[Variadic]
[Node("Math/Matrices/Multiply Matrices")]
public sealed class MultiplyMatrices : ValueNode
{
    public static float2x2 Run(float2x2 a, float2x2 b) => a * b;
    public static float3x3 Run(float3x3 a, float3x3 b) => a * b;
    public static float4x4 Run(float4x4 a, float4x4 b) => a * b;
}
