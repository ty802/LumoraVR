// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Follows value smoothly over time, easing in and out: a hidden middle closes 1 - e^(-2 * speed * seconds)
// of its gap to value each frame, the way Smooth Toward steps, and the result closes as much of its gap to
// the middle. No Stored Value or event steps it. It remembers between frames and starts where value first
// is; worked out twice in a frame it moves once. A rotation turns the shorter way. -xlinka
[Node("Math/Interpolation/Smooth Follow")]
public sealed class SmoothFollow<T> : MemoryNode
{
    public static bool IsValidGenericType
        => typeof(T) == typeof(float) || typeof(T) == typeof(float2) || typeof(T) == typeof(float3) || typeof(T) == typeof(float4)
            || typeof(T) == typeof(color) || typeof(T) == typeof(colorHDR) || typeof(T) == typeof(floatQ);

    public override void Build(FlowBuilder f)
        => f.Follow(f.Input<T>("value"), f.Input("speed", 4f), f.Output<T>("result"), nameof(SmoothToward), stages: 2, speedScale: 2f);
}
