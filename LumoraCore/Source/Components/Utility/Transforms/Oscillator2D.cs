// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Each component sweeps between two values, at its own rate and phase.
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(-100)]
public class Oscillator2D : Component
{
    public readonly Sync<float2> Min;

    public readonly Sync<float2> Max;

    // In radians per second, per component.
    public readonly Sync<float2> Speed;

    // Radians added to each component's phase.
    public readonly Sync<float2> Phase;

    public readonly FieldDrive<float2> Target;

    public Oscillator2D()
    {
        Min = new Sync<float2>(this, float2.Zero);
        Max = new Sync<float2>(this, float2.One);
        Speed = new Sync<float2>(this, float2.One);
        Phase = new Sync<float2>(this, float2.Zero);
        Target = new FieldDrive<float2>(this) { LocalValueOnly = true };
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

        Target.SetValue(new float2(
            min.x + (max.x - min.x) * Oscillation.Unit(now, speed.x, phase.x),
            min.y + (max.y - min.y) * Oscillation.Unit(now, speed.y, phase.y)));
    }
}
