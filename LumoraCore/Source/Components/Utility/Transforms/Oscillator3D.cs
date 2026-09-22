// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Each component sweeps between two values, at its own rate and phase.
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(-100)]
public class Oscillator3D : Component
{
    public readonly Sync<float3> Min;

    public readonly Sync<float3> Max;

    // In radians per second, per component.
    public readonly Sync<float3> Speed;

    // Radians added to each component's phase.
    public readonly Sync<float3> Phase;

    public readonly FieldDrive<float3> Target;

    public Oscillator3D()
    {
        Min = new Sync<float3>(this, float3.Zero);
        Max = new Sync<float3>(this, float3.One);
        Speed = new Sync<float3>(this, float3.One);
        Phase = new Sync<float3>(this, float3.Zero);
        Target = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;

        double now = UtilityClock.Seconds(World);
        var min = Min.Value;
        var max = Max.Value;
        var speed = Speed.Value;
        var phase = Phase.Value;

        Target.SetValue(new float3(
            min.x + (max.x - min.x) * Oscillation.Unit(now, speed.x, phase.x),
            min.y + (max.y - min.y) * Oscillation.Unit(now, speed.y, phase.y),
            min.z + (max.z - min.z) * Oscillation.Unit(now, speed.z, phase.z)));
    }
}
