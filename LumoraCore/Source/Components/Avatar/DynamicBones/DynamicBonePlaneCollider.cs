// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// An endless plane a dynamic bone chain stays on the front side of. A floor under the avatar's feet keeps
// tails and long hair from sinking through the ground. -xlinka
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBonePlaneCollider : Component, IDynamicBoneCollider
{
    public readonly Sync<float3> Offset;
    public readonly Sync<float3> Normal;

    public DynamicBonePlaneCollider()
    {
        Offset = new Sync<float3>(this, float3.Zero);
        Normal = new Sync<float3>(this, float3.Up);
    }

    public bool TryGetShape(out DynamicBoneColliderShape shape)
    {
        if (!Enabled || Slot == null || Slot.IsDestroyed)
        {
            shape = default;
            return false;
        }

        var normal = Slot.LocalDirectionToGlobal(Normal.Value);
        if (normal.LengthSquared < 1e-12f)
        {
            shape = default;
            return false;
        }
        float3 point = Slot.LocalPointToGlobal(Offset.Value);
        shape = DynamicBoneColliderShape.FromPlane(in point, normal.Normalized);
        return true;
    }
}
