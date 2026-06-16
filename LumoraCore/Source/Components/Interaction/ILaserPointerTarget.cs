// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

public readonly struct LaserPointerHit
{
    public readonly float Distance;
    public readonly float3 Point;

    public LaserPointerHit(float distance, in float3 point)
    {
        Distance = distance;
        Point = point;
    }
}

// implemented by engine UI roots that want the laser to drive pointer state. - xlinka
public interface ILaserPointerTarget : IInteractionTarget
{
    bool TryGetLaserPointerHit(
        InteractionLaser laser,
        in float3 rayOrigin,
        in float3 rayDirection,
        float maxDistance,
        out LaserPointerHit hit);

    void UpdateLaserPointer(
        InteractionLaser laser,
        int pointerId,
        in float3 rayOrigin,
        in float3 rayDirection,
        bool isPressed);

    void ClearLaserPointer(InteractionLaser laser, int pointerId);
}

public interface ILaserAxisTarget
{
    bool ProcessLaserAxis(InteractionLaser laser, int pointerId, in float2 axis);
}

public interface ILaserSecondaryTarget
{
    bool TriggerLaserSecondary(InteractionLaser laser, int pointerId);
}
