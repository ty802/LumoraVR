// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// An echo as plain numbers: milliseconds between repeats, the fraction of each repeat fed back into the next,
// and the corners of the low-pass (HighCut) and high-pass (LowCut) in the feedback loop, in hertz. Mix runs from
// the dry signal alone (0) through both at full level (0.5) to the echoes alone (1). Clamped pulls anything
// outside the limits in, and a NaN falls back to that number's default.
public struct DelaySettings : IEquatable<DelaySettings>
{
    public const float MinTime = 1f;
    public const float MaxTime = 2000f;
    public const float MinFeedback = 0f;
    public const float MaxFeedback = 0.95f;
    public const float MinHighCut = 200f;
    public const float MaxHighCut = 20000f;
    public const float MinLowCut = 20f;
    public const float MaxLowCut = 2000f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;

    public const float DefaultTime = 350f;
    public const float DefaultFeedback = 0.35f;
    public const float DefaultHighCut = 8000f;
    public const float DefaultLowCut = 80f;
    public const bool DefaultPingPong = false;
    public const float DefaultMix = 0.35f;

    public float Time;
    public float Feedback;
    public float HighCut;
    public float LowCut;

    // Repeats bounce left, right, left from a mono sum of the input instead of each side echoing itself.
    public bool PingPong;

    public float Mix;

    public static DelaySettings Default => new()
    {
        Time = DefaultTime,
        Feedback = DefaultFeedback,
        HighCut = DefaultHighCut,
        LowCut = DefaultLowCut,
        PingPong = DefaultPingPong,
        Mix = DefaultMix,
    };

    public readonly DelaySettings Clamped() => new()
    {
        Time = DspMath.Limit(Time, MinTime, MaxTime, DefaultTime),
        Feedback = DspMath.Limit(Feedback, MinFeedback, MaxFeedback, DefaultFeedback),
        HighCut = DspMath.Limit(HighCut, MinHighCut, MaxHighCut, DefaultHighCut),
        LowCut = DspMath.Limit(LowCut, MinLowCut, MaxLowCut, DefaultLowCut),
        PingPong = PingPong,
        Mix = DspMath.Limit(Mix, MinMix, MaxMix, DefaultMix),
    };

    public readonly bool IsFinite
        => float.IsFinite(Time) && float.IsFinite(Feedback) && float.IsFinite(HighCut) && float.IsFinite(LowCut) && float.IsFinite(Mix);

    public readonly bool Equals(DelaySettings other)
        => Time.Equals(other.Time) && Feedback.Equals(other.Feedback) && HighCut.Equals(other.HighCut) && LowCut.Equals(other.LowCut)
           && PingPong == other.PingPong && Mix.Equals(other.Mix);

    public override readonly bool Equals(object? obj) => obj is DelaySettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Time, Feedback, HighCut, LowCut, PingPong, Mix);

    public static bool operator ==(DelaySettings left, DelaySettings right) => left.Equals(right);

    public static bool operator !=(DelaySettings left, DelaySettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"{Time} ms, feedback {Feedback}, high cut {HighCut} Hz, low cut {LowCut} Hz, {(PingPong ? "ping-pong" : "stereo")}, mix {Mix}";
}
