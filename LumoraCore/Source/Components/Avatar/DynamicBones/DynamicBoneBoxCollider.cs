// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Oriented box a soft body / dynamic bone collides against. Unlike a movement raycast this handles
// RESTING and already-penetrating particles (it pushes a point out of the box's nearest face), so
// cloth drapes over a box and rests on top without sinking through it.
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

    public bool TryGetShape(out DynamicBoneColliderShape shape)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
        {
            shape = default;
            return false;
        }

        var gs = Slot.GlobalScale;
        var size = Size.Value;
        var halfSize = new float3(
            System.MathF.Abs(size.x) * 0.5f,
            System.MathF.Abs(size.y) * 0.5f,
            System.MathF.Abs(size.z) * 0.5f);
        var invScale = new float3(
            1f / System.MathF.Max(System.MathF.Abs(gs.x), 1e-4f),
            1f / System.MathF.Max(System.MathF.Abs(gs.y), 1e-4f),
            1f / System.MathF.Max(System.MathF.Abs(gs.z), 1e-4f));

        shape = DynamicBoneColliderShape.FromBox(
            Slot.LocalToWorld, Slot.WorldToLocal, Offset.Value, in halfSize, in invScale);
        return true;
    }
}
