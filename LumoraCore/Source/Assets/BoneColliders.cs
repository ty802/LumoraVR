// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Assets;

// A capsule along every bone of a rigged model, so a laser and a hand can find it: a skinned mesh deforms, so
// a collider made from its mesh would stay behind in its rest pose. Each capsule runs from a bone to a child
// bone and is built in the bone's own space, as long as the bone and a fifth as thick, so whatever scale the
// armature carries, or settles to after the import, it stays on the bone and moves with it. Query-only: found
// by hands and lasers, never walked into. -xlinka
internal static class BoneColliders
{
    public const string SlotName = "BoneCollider";

    // Shorter than this in the world and the segment is a twist or a helper bone, not a part of the body.
    private const float MinWorldLength = 0.01f;
    private const float RadiusRatio = 0.2f;

    public static int Build(IEnumerable<Slot> bones)
    {
        var set = new HashSet<Slot>();
        foreach (var bone in bones)
        {
            if (bone != null && !bone.IsDestroyed)
                set.Add(bone);
        }
        int made = 0;
        var children = new List<Slot>();
        foreach (var bone in set)
        {
            // A copy: the capsules go on as children of the bone being walked.
            children.Clear();
            children.AddRange(bone.Children);
            foreach (var child in children)
            {
                if (!set.Contains(child))
                    continue;
                var span = child.LocalPosition.Value;
                float length = span.Length;
                if (length < 1e-6f || length * bone.GlobalScale.x < MinWorldLength)
                    continue;
                var slot = bone.AddSlot(SlotName);
                slot.LocalPosition.Value = span * 0.5f;
                slot.LocalRotation.Value = UpTo(span / length);
                var capsule = slot.AttachComponent<CapsuleCollider>();
                capsule.Type.Value = ColliderType.Trigger;
                capsule.Radius.Value = length * RadiusRatio;
                capsule.Height.Value = length;
                made++;
            }
        }
        return made;
    }

    // The turn that carries +Y, the capsule's axis, onto a direction.
    private static floatQ UpTo(float3 direction)
    {
        float dot = float3.Dot(float3.Up, direction);
        if (dot > 0.99999f)
            return floatQ.Identity;
        if (dot < -0.99999f)
            return floatQ.AxisAngle(float3.Right, System.MathF.PI);
        var axis = float3.Cross(float3.Up, direction);
        return new floatQ(axis.x, axis.y, axis.z, 1f + dot).Normalized;
    }
}
