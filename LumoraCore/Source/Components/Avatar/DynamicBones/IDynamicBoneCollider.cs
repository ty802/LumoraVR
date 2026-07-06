// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>A shape dynamic-bone particles collide against (world space push-out).</summary>
public interface IDynamicBoneCollider : IWorldElement
{
    /// <summary>Push the particle out of this collider. Returns true when a correction was applied.</summary>
    bool ResolveParticle(ref float3 worldPosition, float particleRadius);
}
