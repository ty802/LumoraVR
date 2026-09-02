// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A shape dynamic-bone particles collide against (world space push-out).
public interface IDynamicBoneCollider : IWorldElement
{
    // Freeze this collider into world space for the frame. False when it is disabled or its slot is
    // gone. Callers snapshot once per frame and test every particle against the result; the per
    // particle path must never reach back into the slot.
    bool TryGetShape(out DynamicBoneColliderShape shape);

    // One-shot convenience for callers that are not batching. Prefer TryGetShape plus Resolve.
    bool ResolveParticle(ref float3 worldPosition, float particleRadius)
        => TryGetShape(out var shape) && shape.Resolve(ref worldPosition, particleRadius);
}
