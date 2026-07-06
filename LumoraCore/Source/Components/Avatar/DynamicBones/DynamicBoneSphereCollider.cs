// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Sphere a dynamic bone chain collides against - put these on the head, chest and hips so hair and
/// tails don't clip through the body.
/// </summary>
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBoneSphereCollider : Component, IDynamicBoneCollider
{
    public readonly Sync<float> Radius;
    public readonly Sync<float3> Offset;

    public DynamicBoneSphereCollider()
    {
        Radius = new Sync<float>(this, 0.1f);
        Offset = new Sync<float3>(this, float3.Zero);
    }

    public bool ResolveParticle(ref float3 worldPosition, float particleRadius)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
            return false;

        float3 center = Slot.LocalPointToGlobal(Offset.Value);
        var gs = Slot.GlobalScale;
        float radius = Radius.Value * (System.MathF.Abs(gs.x) + System.MathF.Abs(gs.y) + System.MathF.Abs(gs.z)) / 3f;

        float minDist = radius + particleRadius;
        float3 delta = worldPosition - center;
        float distSq = delta.LengthSquared;
        if (distSq >= minDist * minDist)
            return false;

        float dist = System.MathF.Sqrt(distSq);
        float3 dir = dist > 1e-6f ? delta / dist : float3.Up;
        worldPosition = center + dir * minDist;
        return true;
    }
}
