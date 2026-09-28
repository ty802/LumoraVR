// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// Degrees about x, y and z.
[Node("Math/Rotations/To Euler")]
public sealed class ToEuler : ValueNode
{
    [return: Port("degrees")]
    public static float3 Run(floatQ rotation) => rotation.ToEuler() * (180f / MathF.PI);
}
