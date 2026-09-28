// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// Two numbers are as far apart as their difference without its sign.
[Node("Math/Vectors/Distance")]
public sealed class Distance : ValueNode
{
    [return: Port("distance")] public static float Run(float2 a, float2 b) => float2.Distance(a, b);
    [return: Port("distance")] public static float Run(float3 a, float3 b) => float3.Distance(a, b);
    [return: Port("distance")] public static float Run(float4 a, float4 b) => float4.Distance(a, b);
    [return: Port("distance")] public static float Run(float a, float b) => MathF.Abs(a - b);
    [return: Port("distance")] public static double Run(double a, double b) => Math.Abs(a - b);
}
