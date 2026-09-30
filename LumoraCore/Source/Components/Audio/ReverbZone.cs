// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Audio;

// A place with its own acoustics. Within Radius of the slot a listener hears this reverb in full, across
// BlendDistance beyond that it eases away, and past that it is gone; both follow the slot's scale, and an
// endless Radius covers the whole world. Every listener on a machine is weighed where it stands, so a
// camera across the hall hears the hall and not the booth the user is in. The reverb runs after the
// listener's own effects, and walking out lets its tail ring out rather than cutting it.
//
// Where zones overlap, the one with the highest Priority takes its weight ahead of the rest, which share
// what is left, so a booth inside a hall replaces the hall instead of stacking on it. Zones of equal priority
// split what is left in proportion when they ask for more than there is.
//
// The fields replicate and save like any others. The reverb itself is built on each machine, one per
// listener that can hear the zone. -xlinka
[ComponentCategory("Audio")]
public class ReverbZone : Component
{
    // Metres at scale 1.
    public readonly Sync<float> Radius;
    public readonly Sync<float> BlendDistance;

    public readonly Sync<int> Priority;

    [Range(ReverbSettings.MinPreDelay, ReverbSettings.MaxPreDelay, "0")]
    public readonly Sync<float> PreDelay;

    [Range(ReverbSettings.MinCrossover, ReverbSettings.MaxCrossover, "0", exponential: true)]
    public readonly Sync<float> Crossover;

    [Range(ReverbSettings.MinRT60, ReverbSettings.MaxRT60, "0.00", exponential: true)]
    public readonly Sync<float> LowRT60;

    [Range(ReverbSettings.MinRT60, ReverbSettings.MaxRT60, "0.00", exponential: true)]
    public readonly Sync<float> MidRT60;

    // Where the decay time is half the mid one.
    [Range(ReverbSettings.MinHFDamping, ReverbSettings.MaxHFDamping, "0", exponential: true)]
    public readonly Sync<float> HFDamping;

    [Range(ReverbSettings.MinEQFrequency, ReverbSettings.MaxEQFrequency, "0", exponential: true)]
    public readonly Sync<float> EQ1Frequency;

    [Range(ReverbSettings.MinEQLevel, ReverbSettings.MaxEQLevel, "0.0")]
    public readonly Sync<float> EQ1Level;

    [Range(ReverbSettings.MinEQFrequency, ReverbSettings.MaxEQFrequency, "0", exponential: true)]
    public readonly Sync<float> EQ2Frequency;

    [Range(ReverbSettings.MinEQLevel, ReverbSettings.MaxEQLevel, "0.0")]
    public readonly Sync<float> EQ2Level;

    [Range(ReverbSettings.MinMix, ReverbSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    [Range(ReverbSettings.MinLevel, ReverbSettings.MaxLevel, "0.0")]
    public readonly Sync<float> Level;

    // Bumped on every change pass, so the manager re-reads the reverb fields only when they may have moved.
    internal int SettingsVersion;

    internal bool InManager;

    public ReverbZone()
    {
        Radius = new Sync<float>(this, 5f);
        BlendDistance = new Sync<float>(this, 2f);
        Priority = new Sync<int>(this, 0);
        PreDelay = new Sync<float>(this, ReverbSettings.DefaultPreDelay);
        Crossover = new Sync<float>(this, ReverbSettings.DefaultCrossover);
        LowRT60 = new Sync<float>(this, ReverbSettings.DefaultLowRT60);
        MidRT60 = new Sync<float>(this, ReverbSettings.DefaultMidRT60);
        HFDamping = new Sync<float>(this, ReverbSettings.DefaultHFDamping);
        EQ1Frequency = new Sync<float>(this, ReverbSettings.DefaultEQ1Frequency);
        EQ1Level = new Sync<float>(this, ReverbSettings.DefaultEQ1Level);
        EQ2Frequency = new Sync<float>(this, ReverbSettings.DefaultEQ2Frequency);
        EQ2Level = new Sync<float>(this, ReverbSettings.DefaultEQ2Level);
        Mix = new Sync<float>(this, ReverbSettings.DefaultMix);
        Level = new Sync<float>(this, ReverbSettings.DefaultLevel);
    }

    // The fields as stored; the filter clamps what it is given to ReverbSettings' limits.
    public ReverbSettings Settings => Fields.Read();

    internal ReverbFields Fields
        => new(PreDelay, Crossover, LowRT60, MidRT60, HFDamping, EQ1Frequency, EQ1Level, EQ2Frequency, EQ2Level, Mix, Level);

    public bool IsZoneActive => !IsDestroyed && Enabled.Value && Slot != null && Slot.IsActive;

    // 1 within the radius, easing to 0 across the blend band, 0 beyond it.
    public float WeightAt(float3 point)
    {
        var slot = Slot;
        if (slot == null)
            return 0f;
        float scale = ScaleOf(slot);
        float radius = Positive(Radius.Value) * scale;
        float band = Positive(BlendDistance.Value) * scale;
        float distance = (point - slot.GlobalPosition).Length;
        if (float.IsNaN(distance))
            return 0f;
        if (distance <= radius)
            return 1f;
        if (!(band > 0f) || float.IsInfinity(distance))
            return 0f;
        float t = (distance - radius) / band;
        if (!(t < 1f))
            return 0f;
        return 1f - t * t * (3f - 2f * t);
    }

    public override void OnStart()
    {
        base.OnStart();
        World?.Audio?.RegisterZone(this);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        SettingsVersion++;
    }

    public override void OnDestroy()
    {
        World?.Audio?.UnregisterZone(this);
        base.OnDestroy();
    }

    // A slot scaled to nothing has no room left in it; a scale that is not a number is taken as 1.
    private static float ScaleOf(Slot slot)
    {
        var scale = slot.GlobalScale;
        float average = (MathF.Abs(scale.x) + MathF.Abs(scale.y) + MathF.Abs(scale.z)) / 3f;
        return float.IsNaN(average) || float.IsInfinity(average) ? 1f : average;
    }

    private static float Positive(float value) => float.IsNaN(value) || value < 0f ? 0f : value;
}
