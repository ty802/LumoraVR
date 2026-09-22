// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class TimeSine : Component
{
    // Oscillations per second times two pi. Zero holds at the phase offset.
    public readonly Sync<float> Speed;

    public readonly Sync<float> Min;

    public readonly Sync<float> Max;

    // Radians added to the phase, for running several of these out of step.
    public readonly Sync<float> Phase;

    public readonly FieldDrive<float> Target;

    public TimeSine()
    {
        Speed = new Sync<float>(this, 1f);
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
        Phase = new Sync<float>(this, 0f);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        // Reduce in double before the cast: the clock is wall-clock anchored, and a float holding tens of
        // billions of seconds cannot tell one frame from the next, so the sine would sit still.
        double angle = (UtilityClock.Seconds(World) * Speed.Value + Phase.Value) % (2.0 * System.Math.PI);
        float unit = (float)((System.Math.Sin(angle) + 1.0) * 0.5);
        Target.SetValue(Min.Value + (Max.Value - Min.Value) * unit);
    }
}
