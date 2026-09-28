// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// A min above max gives min, where Math.Clamp would throw. Vectors and colors go lane by lane.
[Node("Math/Clamp")]
public sealed class Clamp : ValueNode
{
    public static int Run(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    public static long Run(long value, long min, long max) => Math.Max(min, Math.Min(max, value));
    public static float Run(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
    public static double Run(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    public static float2 Run(float2 value, float2 min, float2 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y));

    public static float3 Run(float3 value, float3 min, float3 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y), Run(value.z, min.z, max.z));

    public static float4 Run(float4 value, float4 min, float4 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y), Run(value.z, min.z, max.z), Run(value.w, min.w, max.w));

    public static int2 Run(int2 value, int2 min, int2 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y));

    public static int3 Run(int3 value, int3 min, int3 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y), Run(value.z, min.z, max.z));

    public static int4 Run(int4 value, int4 min, int4 max)
        => new(Run(value.x, min.x, max.x), Run(value.y, min.y, max.y), Run(value.z, min.z, max.z), Run(value.w, min.w, max.w));

    public static color Run(color value, color min, color max)
        => new(Run(value.r, min.r, max.r), Run(value.g, min.g, max.g), Run(value.b, min.b, max.b), Run(value.a, min.a, max.a));

    public static colorHDR Run(colorHDR value, colorHDR min, colorHDR max)
        => new(Run(value.r, min.r, max.r), Run(value.g, min.g, max.g), Run(value.b, min.b, max.b), Run(value.a, min.a, max.a));
}
