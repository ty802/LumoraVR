// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Circle of a given radius around an axis. The item swings out onto the rim.
[ComponentCategory("Interaction/Magnets")]
public class MagnetRing : MagnetGuide
{
    // In local space.
    public readonly Sync<float> Radius;

    // In local space.
    public readonly Sync<float3> Axis;

    public MagnetRing()
    {
        Radius = new Sync<float>(this, 0.5f);
        Axis = new Sync<float3>(this, float3.Up);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var axis = Axis.Value;
        if (axis.LengthSquared < 1e-10f)
            return false;
        axis = axis.Normalized;

        var local = slot.GlobalPointToLocal(worldPosition);
        var radial = local - axis * float3.Dot(local, axis);
        // Dead on the axis there is no angle to pick, so the ring has nothing to say.
        if (radial.LengthSquared < 1e-10f)
            return false;

        var outward = radial.Normalized;
        position = slot.LocalPointToGlobal(outward * Radius.Value);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(outward, axis);
        return true;
    }
}
