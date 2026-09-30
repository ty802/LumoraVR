// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Shading;

[Node("Vector/Cross Product")]
public sealed class CrossProduct : ShaderNode
{
    [Glsl("cross({a}, {b})")]
    public static float3 Run(float3 a, float3 b) => throw GpuOnly;
}
