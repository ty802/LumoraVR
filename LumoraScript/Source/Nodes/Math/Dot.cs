// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// An integer dot product wraps past the ends of int.
[Node("Math/Vectors/Dot")]
public sealed class Dot : ValueNode
{
    public static float Run(float2 a, float2 b) => float2.Dot(a, b);
    public static float Run(float3 a, float3 b) => float3.Dot(a, b);
    public static float Run(float4 a, float4 b) => float4.Dot(a, b);
    public static int Run(int2 a, int2 b) => unchecked(a.x * b.x + a.y * b.y);
    public static int Run(int3 a, int3 b) => unchecked(a.x * b.x + a.y * b.y + a.z * b.z);
    public static int Run(int4 a, int4 b) => unchecked(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
}
