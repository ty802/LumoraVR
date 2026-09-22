// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Infinite surface through this slot. The item drops onto it along the normal.
[ComponentCategory("Interaction/Magnets")]
public class MagnetPlane : MagnetGuide
{
    // In local space. Up makes this a table top.
    public readonly Sync<float3> Normal;

    public MagnetPlane()
    {
        Normal = new Sync<float3>(this, float3.Up);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var normal = Normal.Value;
        if (normal.LengthSquared < 1e-10f)
            return false;
        normal = normal.Normalized;

        var local = slot.GlobalPointToLocal(worldPosition);
        position = slot.LocalPointToGlobal(local - normal * float3.Dot(local, normal));

        // Any direction lying in the plane will do for forward; take the guide's own forward and
        // flatten it, falling back to its right when the two are parallel.
        var forward = float3.Forward - normal * float3.Dot(float3.Forward, normal);
        if (forward.LengthSquared < 1e-8f)
            forward = float3.Right - normal * float3.Dot(float3.Right, normal);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(forward, normal);
        return true;
    }
}
