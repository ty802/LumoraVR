// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Convention across every shape here: a SURFACE guide (plane, sphere) hands back a rotation whose
// UP is the surface normal, because you set things down on a surface. A DIRECTIONAL guide (line,
// ring) hands back one whose FORWARD runs along the rail or out along the radius, because you line
// things up with a direction. Nothing else has to remember which is which. -xlinka

// Single fixed pose. The item lands exactly on this slot.
[ComponentCategory("Interaction/Magnets")]
public class MagnetPoint : MagnetGuide
{
    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
        {
            position = worldPosition;
            rotation = worldRotation;
            return false;
        }
        position = slot.GlobalPosition;
        rotation = slot.GlobalRotation;
        return true;
    }
}
