// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives a float that sweeps back and forth between two values.
//
// Driven off the shared clock, so every peer sees the same phase and a late joiner does not start the
// sweep from its beginning. -xlinka
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(-100)]
public class Oscillator1D : Component
{
    public readonly Sync<float> Min;

    public readonly Sync<float> Max;

    // In radians per second.
    public readonly Sync<float> Speed;

    // Radians added to the phase, for running several of these out of step.
    public readonly Sync<float> Phase;

    public readonly FieldDrive<float> Target;

    public Oscillator1D()
    {
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
        Speed = new Sync<float>(this, 1f);
        Phase = new Sync<float>(this, 0f);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        float unit = Oscillation.Unit(UtilityClock.Seconds(World), Speed.Value, Phase.Value);
        Target.SetValue(Min.Value + (Max.Value - Min.Value) * unit);
    }
}
