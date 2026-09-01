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

// Drives an integer that counts up with session time, optionally wrapping or bouncing.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class TimeInteger : Component
{
    // Counts per second.
    public readonly Sync<float> Scale;

    // Wrap back to zero after this many counts. Zero or less counts up forever.
    public readonly Sync<int> Repeat;

    // Count back down instead of jumping to zero at the end of a cycle.
    public readonly Sync<bool> PingPong;

    public readonly FieldDrive<int> Target;

    public TimeInteger()
    {
        Scale = new Sync<float>(this, 1f);
        Repeat = new Sync<int>(this, 0);
        PingPong = new Sync<bool>(this, false);
        Target = new FieldDrive<int>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;

        // The clock is wall-clock anchored, so the raw count is in the tens of billions and does not fit
        // an int; wrap in long and only then narrow.
        long raw = (long)System.Math.Floor(UtilityClock.Seconds(World) * Scale.Value);
        int repeat = Repeat.Value;
        int count;
        if (repeat > 0)
        {
            long twice = repeat * 2L;
            bool descending = PingPong.Value && ((raw % twice) + twice) % twice >= repeat;
            count = (int)(((raw % repeat) + repeat) % repeat);
            if (descending)
                count = repeat - 1 - count;
        }
        else
        {
            count = (int)System.Math.Clamp(raw, int.MinValue, int.MaxValue);
        }
        Target.SetValue(count);
    }
}

// Drives a float with the distance walked along a chain of slots.
//
// Runs late so the anchors have already been moved by whatever drives them this frame. A chain rather
// than a fixed pair: two anchors is the common case and falls out of the same sum, and measuring a
// route (hand to elbow to shoulder) otherwise needs a component per segment plus something to add
// them up. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(50)]
public class ElapsedDistance : Component
{
    // In order. Fewer than two measures zero.
    public readonly SyncRefList<Slot> Anchors;

    // Measure in this slot's local space, so the reading follows its scale. Global when empty.
    public readonly SyncRef<Slot> MeasureSpace;

    public readonly FieldDrive<float> Target;

    public ElapsedDistance()
    {
        Anchors = new SyncRefList<Slot>();
        MeasureSpace = new SyncRef<Slot>(this);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public float Distance
    {
        get
        {
            var space = MeasureSpace.Target;
            float total = 0f;
            bool hasPrevious = false;
            float3 previous = float3.Zero;

            foreach (var anchor in Anchors)
            {
                if (anchor == null || anchor.IsDestroyed)
                    continue;
                var point = anchor.GlobalPosition;
                if (space != null && !space.IsDestroyed)
                    point = space.GlobalPointToLocal(point);
                if (hasPrevious)
                    total += (point - previous).Length;
                previous = point;
                hasPrevious = true;
            }
            return total;
        }
    }

    public override void OnUpdate(float delta)
    {
        if (Target.IsLinkValid)
            Target.SetValue(Distance);
    }
}
