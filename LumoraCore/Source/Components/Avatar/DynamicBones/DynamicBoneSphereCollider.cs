// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Sphere a dynamic bone chain collides against - put these on the head, chest and hips so hair and
// tails don't clip through the body.
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBoneSphereCollider : Component, IDynamicBoneCollider
{
    public readonly Sync<float> Radius;
    public readonly Sync<float3> Offset;

    // Keeps particles inside instead of out: hair or fluff that has to stay within this shape.
    public readonly Sync<bool> Inside;

    // Tests each whole bone between two particles, not only the particles, so a long bone cannot slip through
    // between its ends. Off tests the particles alone, the cheaper and older way.
    public readonly Sync<bool> Segments;

    public DynamicBoneSphereCollider()
    {
        Radius = new Sync<float>(this, 0.1f);
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

        float3 centre = Slot.LocalPointToGlobal(Offset.Value);
        var gs = Slot.GlobalScale;
        float radius = Radius.Value * (System.MathF.Abs(gs.x) + System.MathF.Abs(gs.y) + System.MathF.Abs(gs.z)) / 3f;
        shape = DynamicBoneColliderShape.FromSphere(in centre, radius);
        shape.Inside = Inside.Value;
        shape.Segments = Segments.Value;
        return true;
    }
}
