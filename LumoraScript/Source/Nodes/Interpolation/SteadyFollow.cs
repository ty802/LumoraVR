// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Follows value at a steady rate: where it is moves toward value by speed units a second (degrees a
// second for a rotation) and stops on it, the way Move Toward steps. It remembers where it is between
// frames and starts where value first is; worked out twice in a frame it moves once. -xlinka
[Node("Math/Interpolation/Steady Follow")]
public sealed class SteadyFollow<T> : MemoryNode
{
    public static bool IsValidGenericType
        => typeof(T) == typeof(float) || typeof(T) == typeof(float2) || typeof(T) == typeof(float3) || typeof(T) == typeof(float4)
            || typeof(T) == typeof(color) || typeof(T) == typeof(colorHDR) || typeof(T) == typeof(floatQ);

    public override void Build(FlowBuilder f)
        => f.Follow(f.Input<T>("value"), f.Input("speed", 1f), f.Output<T>("result"), nameof(MoveToward));
}
