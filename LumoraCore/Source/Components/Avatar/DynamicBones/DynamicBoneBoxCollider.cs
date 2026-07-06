// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Oriented box a soft body / dynamic bone collides against. Unlike a movement raycast this handles
/// RESTING and already-penetrating particles (it pushes a point out of the box's nearest face), so
/// cloth drapes over a box and rests on top without sinking through it.
/// </summary>
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBoneBoxCollider : Component, IDynamicBoneCollider
{
    public readonly Sync<float3> Size;
    public readonly Sync<float3> Offset;

    public DynamicBoneBoxCollider()
    {
        Size = new Sync<float3>(this, new float3(0.5f, 0.5f, 0.5f));
        Offset = new Sync<float3>(this, float3.Zero);
    }

    public bool ResolveParticle(ref float3 worldPosition, float particleRadius)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
            return false;

        var gs = Slot.GlobalScale;
        // Box in the slot's local frame; transform the particle into it.
        float3 local = Slot.GlobalPointToLocal(worldPosition) - Offset.Value;
        float3 half = new float3(
            System.MathF.Abs(Size.Value.x) * 0.5f + particleRadius / System.MathF.Max(System.MathF.Abs(gs.x), 1e-4f),
            System.MathF.Abs(Size.Value.y) * 0.5f + particleRadius / System.MathF.Max(System.MathF.Abs(gs.y), 1e-4f),
            System.MathF.Abs(Size.Value.z) * 0.5f + particleRadius / System.MathF.Max(System.MathF.Abs(gs.z), 1e-4f));

        // Outside on any axis -> not penetrating.
        if (System.MathF.Abs(local.x) >= half.x || System.MathF.Abs(local.y) >= half.y || System.MathF.Abs(local.z) >= half.z)
            return false;

        // Inside: push out along the axis of least penetration.
        float px = half.x - System.MathF.Abs(local.x);
        float py = half.y - System.MathF.Abs(local.y);
        float pz = half.z - System.MathF.Abs(local.z);
        if (px <= py && px <= pz)
            local.x = System.MathF.CopySign(half.x, local.x);
        else if (py <= pz)
            local.y = System.MathF.CopySign(half.y, local.y);
        else
            local.z = System.MathF.CopySign(half.z, local.z);

        worldPosition = Slot.LocalPointToGlobal(local + Offset.Value);
        return true;
    }
}
