// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Shared base for the components that map one float input range onto an output range.
//
// One float in, any shape out. A per-component input would need a separate component per output type
// AND per input type, and the thing people actually wire up is a single number (a slider, a distance,
// a progress) opening onto a position, a colour or a rotation. -xlinka
public abstract class RangeMapBase : Component
{
    public readonly SyncRef<IField<float>> Source;

    // Input value that maps to the output minimum.
    public readonly Sync<float> SourceMin;

    // Input value that maps to the output maximum.
    public readonly Sync<float> SourceMax;

    // Hold the output at its ends when the input leaves the source range.
    public readonly Sync<bool> Clamp;

    protected RangeMapBase()
    {
        Source = new SyncRef<IField<float>>(this);
        SourceMin = new Sync<float>(this, 0f);
        SourceMax = new Sync<float>(this, 1f);
        Clamp = new Sync<bool>(this, true);
    }

    // 0 at the minimum and 1 at the maximum.
    protected float Progress
    {
        get
        {
            var source = Source.Target;
            float value = source?.Value ?? 0f;
            float span = SourceMax.Value - SourceMin.Value;
            // A zero-width source range has no position to report. Sit at the start rather than
            // dividing by zero and driving the target to NaN, which propagates into transforms and is
            // very hard to trace back here.
            float unit = span == 0f ? 0f : (value - SourceMin.Value) / span;
            return Clamp.Value ? LuminaMath.Clamp(unit, 0f, 1f) : unit;
        }
    }
}
