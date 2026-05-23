// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Helio.UI;

public readonly struct UIInteractionContext
{
    public readonly Canvas Canvas;
    public readonly UIInteractionSource Source;
    public readonly int PointerId;
    public readonly float2 LocalPoint;
    public readonly float3 WorldPoint;
    public readonly float3 RayOrigin;
    public readonly float3 RayDirection;
    public readonly float Distance;

    public UIInteractionContext(
        Canvas canvas,
        UIInteractionSource source,
        int pointerId,
        in float2 localPoint,
        in float3 worldPoint,
        in float3 rayOrigin,
        in float3 rayDirection,
        float distance)
    {
        Canvas = canvas;
        Source = source;
        PointerId = pointerId;
        LocalPoint = localPoint;
        WorldPoint = worldPoint;
        RayOrigin = rayOrigin;
        RayDirection = rayDirection;
        Distance = distance;
    }
}
