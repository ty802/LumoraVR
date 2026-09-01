// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

public static class Oscillation
{
    // 0 at the minimum and 1 at the maximum. The clock is wall-clock anchored, so seconds is a number
    // in the tens of billions; cast to float that has a resolution of thousands of seconds and the sine
    // freezes at one value. Reduce the angle in double first and only then drop to float.
    public static float Unit(double seconds, float speed, float phase)
    {
        double angle = (seconds * speed + phase) % (2.0 * System.Math.PI);
        return (float)((System.Math.Sin(angle) + 1.0) * 0.5);
    }
}

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
