// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Rail between two local points. The item slides to the closest spot along it.
[ComponentCategory("Interaction/Magnets")]
public class MagnetLine : MagnetGuide
{
    // In local space.
    public readonly Sync<float3> Start;

    // In local space.
    public readonly Sync<float3> End;

    // Keep the result between the two points instead of running off along the infinite line.
    public readonly Sync<bool> ClampToSegment;

    public MagnetLine()
    {
        Start = new Sync<float3>(this, new float3(0f, 0f, -0.5f));
        End = new Sync<float3>(this, new float3(0f, 0f, 0.5f));
        ClampToSegment = new Sync<bool>(this, true);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var a = Start.Value;
        var direction = End.Value - a;
        float lengthSq = direction.LengthSquared;
        if (lengthSq < 1e-10f)
            return false;

        var local = slot.GlobalPointToLocal(worldPosition);
        float t = float3.Dot(local - a, direction) / lengthSq;
        if (ClampToSegment.Value)
            t = System.Math.Clamp(t, 0f, 1f);

        position = slot.LocalPointToGlobal(a + direction * t);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(direction, float3.Up);
        return true;
    }
}
