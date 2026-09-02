// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Capsule dynamic-bone collider along the slot's local Y (Height = total end to end). One capsule
// covers a limb or torso where a row of spheres would be needed otherwise.
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

    public bool TryGetShape(out DynamicBoneColliderShape shape)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
        {
            shape = default;
            return false;
        }

        var gs = Slot.GlobalScale;
        float scale = (System.MathF.Abs(gs.x) + System.MathF.Abs(gs.y) + System.MathF.Abs(gs.z)) / 3f;
        float radius = Radius.Value * scale;
        float half = System.MathF.Max(0f, Height.Value * 0.5f - Radius.Value);

        float3 a = Slot.LocalPointToGlobal(Offset.Value + new float3(0f, half, 0f));
        float3 b = Slot.LocalPointToGlobal(Offset.Value - new float3(0f, half, 0f));
        shape = DynamicBoneColliderShape.FromCapsule(in a, in b, radius);
        return true;
    }
}
