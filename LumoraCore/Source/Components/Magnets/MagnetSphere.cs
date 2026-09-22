// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Shell of a given radius. The item is pushed out onto the surface, standing on it.
[ComponentCategory("Interaction/Magnets")]
public class MagnetSphere : MagnetGuide
{
    // In local space.
    public readonly Sync<float> Radius;

    public MagnetSphere()
    {
        Radius = new Sync<float>(this, 0.5f);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var local = slot.GlobalPointToLocal(worldPosition);
        if (local.LengthSquared < 1e-10f)
            return false;

        var outward = local.Normalized;
        position = slot.LocalPointToGlobal(outward * Radius.Value);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(float3.Forward, outward);
        return true;
    }
}
