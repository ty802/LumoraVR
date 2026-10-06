// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Drives a float that climbs at a constant rate and wraps over a span, or swings back down it: a progress
// that loops, a gradient that cycles. The one-number sibling of UVPanner, on the same session clock, so every
// peer sees the same value at the same moment and a late joiner does not start from zero. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class ValuePanner : Component
{
    // Units per second.
    public readonly Sync<float> Speed;

    // Added before the value wraps, for running several of these out of step.
    public readonly Sync<float> StartOffset;

    // The span the value wraps over. Zero or less wraps over one.
    public readonly Sync<float> Repeat;

    // Run back down the span instead of jumping to its start.
    public readonly Sync<bool> PingPong;

    // Added after the value wraps.
    public readonly Sync<float> Offset;

    public readonly FieldDrive<float> Target;

    public ValuePanner()
    {
        Speed = new Sync<float>(this, 1f);
        StartOffset = new Sync<float>(this, 0f);
        Repeat = new Sync<float>(this, 1f);
        PingPong = new Sync<bool>(this, false);
        Offset = new Sync<float>(this, 0f);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public float Value
    {
        get
        {
            // The clock is wall-clock anchored, so the raw product is huge; wrap in double before narrowing.
            double span = Repeat.Value > 0f ? Repeat.Value : 1.0;
            bool pingPong = PingPong.Value;
            double period = pingPong ? span * 2.0 : span;
            double wrapped = (UtilityClock.Seconds(World) * Speed.Value + StartOffset.Value) % period;
            if (wrapped < 0.0)
                wrapped += period;
            if (pingPong && wrapped > span)
                wrapped = period - wrapped;
            return (float)wrapped + Offset.Value;
        }
    }

    public override void OnUpdate(float delta)
    {
        if (Target.IsLinkValid)
            Target.SetValue(Value);
    }
}
