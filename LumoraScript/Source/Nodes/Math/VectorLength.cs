// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Math/Vectors/Length")]
public sealed class VectorLength : ValueNode
{
    [return: Port("length")] public static float Run(float2 vector) => vector.Length;
    [return: Port("length")] public static float Run(float3 vector) => vector.Length;
    [return: Port("length")] public static float Run(float4 vector) => vector.Length;
}
