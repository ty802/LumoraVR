// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Stretches x, y and z, each by its own amount, as a 4x4 matrix that moves nothing.
[Node("Math/Matrices/Compose Scale 4x4")]
public sealed class ComposeScale4x4 : ValueNode
{
    [return: Port("matrix")] public static float4x4 Run(float3 scale) => float4x4.Scale(scale);
}
