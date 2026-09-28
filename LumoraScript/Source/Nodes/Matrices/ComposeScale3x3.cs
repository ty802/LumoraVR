// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Stretches x, y and z, each by its own amount.
[Node("Math/Matrices/Compose Scale 3x3")]
public sealed class ComposeScale3x3 : ValueNode
{
    [return: Port("matrix")] public static float3x3 Run(float3 scale) => float3x3.Scale(scale);
}
