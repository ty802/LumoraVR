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

    // Keeps particles inside instead of out: hair or fluff that has to stay within this shape.
    public readonly Sync<bool> Inside;

    // Tests each whole bone between two particles, not only the particles, so a long bone cannot slip through
    // between its ends. Off tests the particles alone, the cheaper and older way.
    public readonly Sync<bool> Segments;

    public DynamicBoneCapsuleCollider()
    {
        Radius = new Sync<float>(this, 0.1f);
        Height = new Sync<float>(this, 0.4f);
        Offset = new Sync<float3>(this, float3.Zero);
        Inside = new Sync<bool>(this, false);
        Segments = new Sync<bool>(this, false);
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
        shape.Inside = Inside.Value;
        shape.Segments = Segments.Value;
        return true;
    }
}
