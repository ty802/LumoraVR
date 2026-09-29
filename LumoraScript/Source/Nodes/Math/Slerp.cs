// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// From a to b by t, along the shortest turn.
[Node("Math/Rotations/Slerp")]
public sealed class Slerp : ValueNode
{
    [return: Port("rotation")]
    public static floatQ Run(floatQ a, floatQ b, float t) => floatQ.Slerp(a, b, t);

    // Vectors turn from a's direction to b's along the great circle at an even rate while the length
    // goes straight from a's to b's, so unit vectors stay unit. t past 0 to 1 turns on round the circle.
    // A color turns as a four lane vector.
    public static float2 Run(float2 a, float2 b, float t) => SphericalBlend.Vectors(a, b, t);
    public static float3 Run(float3 a, float3 b, float t) => SphericalBlend.Vectors(a, b, t);
    public static float4 Run(float4 a, float4 b, float t) => SphericalBlend.Vectors(a, b, t);
    public static color Run(color a, color b, float t) => SphericalBlend.Vectors(a, b, t);
    public static colorHDR Run(colorHDR a, colorHDR b, float t) => SphericalBlend.Vectors(a, b, t);
}
