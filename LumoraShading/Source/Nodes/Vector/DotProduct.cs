// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Shading;

[Node("Vector/Dot Product")]
public sealed class DotProduct : ShaderNode
{
    [Glsl("dot({a}, {b})")]
    public static float Run(float3 a, float3 b) => throw GpuOnly;

    [Glsl("dot({a}, {b})")]
    public static float Run(float2 a, float2 b) => throw GpuOnly;

    [Glsl("dot({a}, {b})")]
    public static float Run(float4 a, float4 b) => throw GpuOnly;
}
