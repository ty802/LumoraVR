// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.Utility;

// Drives a bool that flicks on for a moment at random intervals.
//
// The authority publishes WHEN the next pulse starts, as a wall-clock tick count, and every peer
// derives the bool from it. That is one long on the wire per pulse instead of a bool stream, and every
// peer flashes at the same instant. Rolling the interval locally on each peer would look identical on
// one machine and be visibly out of step on any other.
//
// The pulse is a driven value, so it never syncs on its own: an on/off that replicated would fight the
// local derivation and could arrive after the pulse had already ended. -xlinka
[ComponentCategory("Utility/Spawning")]
[DefaultUpdateOrder(-100)]
public class RandomPulse : Component
{
    // In seconds.
    public readonly Sync<float> MinInterval;

    // In seconds.
    public readonly Sync<float> MaxInterval;

    // In seconds.
    public readonly Sync<float> PulseLength;

    // Written by the authority.
    public readonly Sync<long> NextPulseTicks;

    public readonly FieldDrive<bool> Target;

    private readonly Random _random = new();

    public RandomPulse()
    {
        MinInterval = new Sync<float>(this, 0.5f);
        MaxInterval = new Sync<float>(this, 5f);
        PulseLength = new Sync<float>(this, 0.2f);
        NextPulseTicks = new Sync<long>(this, 0L);
        Target = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        double now = UtilityClock.Seconds(World);
        double start = NextPulseTicks.Value / (double)TimeSpan.TicksPerSecond;
        double end = start + System.Math.Max(0f, PulseLength.Value);

        if (World?.IsAuthority == true && (NextPulseTicks.Value == 0L || now >= end))
            ScheduleNext(now);

        Target.SetValue(now >= start && now < end);
    }

    private void ScheduleNext(double now)
    {
        float min = System.Math.Max(0f, MinInterval.Value);
        float max = System.Math.Max(min, MaxInterval.Value);
        double next = now + min + _random.NextDouble() * (max - min);
        NextPulseTicks.Value = (long)(next * TimeSpan.TicksPerSecond);
    }
}
