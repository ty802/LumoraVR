// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// The segments an outlined shape is drawn with: a box's twelve edges and a sphere's three rings, from a
// centre, a size and a rotation, in whatever space the caller works in.
public static class ScriptDrawingShapes
{
    public const int RingSegments = 32;

    public static void BoxEdges(float3 center, float3 size, floatQ rotation, List<(float3 A, float3 B)> into)
    {
        var half = size * 0.5f;
        var corners = new float3[8];
        for (int i = 0; i < 8; i++)
        {
            var offset = new float3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            corners[i] = center + rotation * offset;
        }
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                    into.Add((corners[i], corners[i | bit]));
            }
        }
    }

    public static void Rings(float3 center, float radius, floatQ rotation, List<(float3 A, float3 B)> into)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            float3 previous = default;
            for (int step = 0; step <= RingSegments; step++)
            {
                float angle = MathF.PI * 2f * step / RingSegments;
                float cos = MathF.Cos(angle) * radius;
                float sin = MathF.Sin(angle) * radius;
                var local = axis switch
                {
                    0 => new float3(0f, cos, sin),
                    1 => new float3(cos, 0f, sin),
                    _ => new float3(cos, sin, 0f),
                };
                var point = center + rotation * local;
                if (step > 0)
                    into.Add((previous, point));
                previous = point;
            }
        }
    }
}
