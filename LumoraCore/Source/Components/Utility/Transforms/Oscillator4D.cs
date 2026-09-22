// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Each component sweeps between two values, at its own rate and phase.
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(-100)]
public class Oscillator4D : Component
{
    public readonly Sync<float4> Min;

    public readonly Sync<float4> Max;

    // In radians per second, per component.
    public readonly Sync<float4> Speed;

    // Radians added to each component's phase.
    public readonly Sync<float4> Phase;

    public readonly FieldDrive<float4> Target;

    public Oscillator4D()
    {
        Min = new Sync<float4>(this, float4.Zero);
        Max = new Sync<float4>(this, float4.One);
        Speed = new Sync<float4>(this, float4.One);
        Phase = new Sync<float4>(this, float4.Zero);
        Target = new FieldDrive<float4>(this) { LocalValueOnly = true };
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

        Target.SetValue(new float4(
            min.x + (max.x - min.x) * Oscillation.Unit(now, speed.x, phase.x),
            min.y + (max.y - min.y) * Oscillation.Unit(now, speed.y, phase.y),
            min.z + (max.z - min.z) * Oscillation.Unit(now, speed.z, phase.z),
            min.w + (max.w - min.w) * Oscillation.Unit(now, speed.w, phase.w)));
    }
}
