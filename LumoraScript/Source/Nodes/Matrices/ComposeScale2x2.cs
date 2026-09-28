// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Stretches x and y, each by its own amount.
[Node("Math/Matrices/Compose Scale 2x2")]
public sealed class ComposeScale2x2 : ValueNode
{
    [return: Port("matrix")] public static float2x2 Run(float2 scale) => float2x2.Scale(scale);
}
