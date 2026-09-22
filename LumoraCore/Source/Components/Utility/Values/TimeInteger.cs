// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
