// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Keeps a body pose inside a box around this slot, or puts it there when nothing tracks it.
//
// A tracked pose keeps its own rotation and has its position clamped to a box of BoxSize centred on this
// slot, measured along this slot's axes: a big box lets a tracked hip or foot move freely, a zero box pins
// it to the slot. A pose nothing tracks takes FallbackPose (this slot when empty) whole and counts as
// tracked from here on, which is how a seat puts the hips and feet of somebody without trackers where it
// wants them. Our sockets carry no notion of a simulated pose, so every tracked one is clamped. -xlinka
[ComponentCategory("Users/Avatar")]
public class PoseBoxClamp : Component, IPoseFilter
{
    public readonly Sync<float3> BoxSize = new(float3.Zero);

    // this slot when empty
    public readonly SyncRef<Slot> FallbackPose = new();

    public void ProcessPose(AvatarSocket socket, Slot space, ref float3 position, ref floatQ rotation, ref bool isTracking)
    {
        var slot = Slot;
        if (!Enabled.Value || slot == null || slot.IsDestroyed || space == null)
            return;

        if (isTracking)
        {
            float3 half = BoxSize.Value * 0.5f;
            float3 local = space.LocalPointToSpace(in position, slot);
            local = new float3(
                System.Math.Clamp(local.x, -MathF.Abs(half.x), MathF.Abs(half.x)),
                System.Math.Clamp(local.y, -MathF.Abs(half.y), MathF.Abs(half.y)),
                System.Math.Clamp(local.z, -MathF.Abs(half.z), MathF.Abs(half.z)));
            position = slot.LocalPointToSpace(in local, space);
            return;
        }

        var reference = FallbackPose.Target is { IsDestroyed: false } fallback ? fallback : slot;
        position = space.GlobalPointToLocal(reference.GlobalPosition);
        rotation = space.GlobalRotationToLocal(reference.GlobalRotation);
        isTracking = true;
    }
}
