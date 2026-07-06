// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// Per-world clock, advanced once at the top of each world update. The single source for frame
/// timing: components read <see cref="World.Time"/> instead of threading deltas through call
/// chains or rolling their own tick math.
/// </summary>
public sealed class WorldClock
{
    /// <summary>A hitch/debugger pause never feeds a giant step into simulation code.</summary>
    public const float MaxTimestep = 0.1f;

    /// <summary>Scaled frame step, clamped to <see cref="MaxTimestep"/>. What simulation code wants.</summary>
    public float Delta { get; private set; } = 1f / 60f;

    /// <summary>Scaled frame step, unclamped. What accumulators/measurement want.</summary>
    public float RawDelta { get; private set; } = 1f / 60f;

    /// <summary>Exponentially smoothed step, for rates that should not track single-frame spikes.</summary>
    public float SmoothDelta { get; private set; } = 1f / 60f;

    /// <summary>Seconds of scaled world time since the world started.</summary>
    public double TotalTime { get; private set; }

    /// <summary>World updates since the world started.</summary>
    public ulong UpdateIndex { get; private set; }

    /// <summary>Smoothed updates-per-second.</summary>
    public float FramesPerSecond { get; private set; } = 60f;

    internal void Advance(double scaledDelta)
    {
        float raw = (float)scaledDelta;
        RawDelta = raw;
        Delta = raw < 0f ? 0f : (raw > MaxTimestep ? MaxTimestep : raw);
        if (raw > 1e-6f)
        {
            SmoothDelta += (raw - SmoothDelta) * 0.1f;
            FramesPerSecond += (1f / raw - FramesPerSecond) * 0.05f;
        }
        TotalTime += scaledDelta;
        UpdateIndex++;
    }
}
