// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Capsule dynamic-bone collider along the slot's local Y (Height = total end to end). One capsule
/// covers a limb or torso where a row of spheres would be needed otherwise.
/// </summary>
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBoneCapsuleCollider : Component, IDynamicBoneCollider
{
    public readonly Sync<float> Radius;
    public readonly Sync<float> Height;
    public readonly Sync<float3> Offset;

    public DynamicBoneCapsuleCollider()
    {
        Radius = new Sync<float>(this, 0.1f);
        Height = new Sync<float>(this, 0.4f);
        Offset = new Sync<float3>(this, float3.Zero);
    }

    public bool ResolveParticle(ref float3 worldPosition, float particleRadius)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
            return false;

        var gs = Slot.GlobalScale;
        float scale = (System.MathF.Abs(gs.x) + System.MathF.Abs(gs.y) + System.MathF.Abs(gs.z)) / 3f;
        float radius = Radius.Value * scale;
        float half = System.MathF.Max(0f, Height.Value * 0.5f - Radius.Value);

        float3 a = Slot.LocalPointToGlobal(Offset.Value + new float3(0f, half, 0f));
        float3 b = Slot.LocalPointToGlobal(Offset.Value - new float3(0f, half, 0f));

        // Closest point on the segment to the particle.
        float3 ab = b - a;
        float abLenSq = ab.LengthSquared;
        float t = abLenSq > 1e-8f ? System.Math.Clamp(float3.Dot(worldPosition - a, ab) / abLenSq, 0f, 1f) : 0f;
        float3 closest = a + ab * t;

        float minDist = radius + particleRadius;
        float3 delta = worldPosition - closest;
        float distSq = delta.LengthSquared;
        if (distSq >= minDist * minDist)
            return false;

        float dist = System.MathF.Sqrt(distSq);
        float3 dir = dist > 1e-6f ? delta / dist : float3.Up;
        worldPosition = closest + dir * minDist;
        return true;
    }
}
