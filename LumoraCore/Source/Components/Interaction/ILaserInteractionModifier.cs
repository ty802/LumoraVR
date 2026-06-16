// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// implemented by a Component on the target side to filter how the laser
// resolves a hit point (e.g. snap to a grid, slow when near a button, reject
// the hit if the surface normal is wrong). - xlinka
public interface ILaserInteractionModifier
{
    float? GetSmoothSpeed(InteractionLaser laser, in float3 newPoint, in float3 oldPoint);

    float3 FilterPoint(InteractionLaser laser, in float3 point);

    bool IsInteractionHit(in float3 point, in float3 direction);
}
