// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A flanger as plain numbers: the delay sweeps from Delay up to Delay + Depth milliseconds and back, Rate times a
// second, and Feedback (either sign) sharpens the comb it makes. Spread moves the right side's sweep up to half a
// cycle away from the left's, Mix crossfades from the dry signal (0) through equal parts (0.5, the deepest notches)
// to the swept copy alone (1). Clamped pulls anything outside the limits in, and a NaN falls back to that number's
// default.
public struct FlangerSettings : IEquatable<FlangerSettings>
{
    public const float MinRate = 0.01f;
    public const float MaxRate = 10f;
    public const float MinDepth = 0f;
    public const float MaxDepth = 10f;
    public const float MinDelay = 0.1f;
    public const float MaxDelay = 10f;
    public const float MinFeedback = -0.95f;
    public const float MaxFeedback = 0.95f;
    public const float MinSpread = 0f;
    public const float MaxSpread = 1f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;

    public const float DefaultRate = 0.25f;
    public const float DefaultDepth = 2f;
    public const float DefaultDelay = 1f;
    public const float DefaultFeedback = 0.5f;
    public const float DefaultSpread = 0f;
    public const float DefaultMix = 0.5f;

    public float Rate;
    public float Depth;
    public float Delay;
    public float Feedback;
    public float Spread;
    public float Mix;

    public static FlangerSettings Default => new()
    {
        Rate = DefaultRate,
        Depth = DefaultDepth,
        Delay = DefaultDelay,
        Feedback = DefaultFeedback,
        Spread = DefaultSpread,
        Mix = DefaultMix,
    };

    public readonly FlangerSettings Clamped() => new()
    {
        Rate = DspMath.Limit(Rate, MinRate, MaxRate, DefaultRate),
        Depth = DspMath.Limit(Depth, MinDepth, MaxDepth, DefaultDepth),
        Delay = DspMath.Limit(Delay, MinDelay, MaxDelay, DefaultDelay),
        Feedback = DspMath.Limit(Feedback, MinFeedback, MaxFeedback, DefaultFeedback),
        Spread = DspMath.Limit(Spread, MinSpread, MaxSpread, DefaultSpread),
        Mix = DspMath.Limit(Mix, MinMix, MaxMix, DefaultMix),
    };

    public readonly bool IsFinite
        => float.IsFinite(Rate) && float.IsFinite(Depth) && float.IsFinite(Delay) && float.IsFinite(Feedback) && float.IsFinite(Spread)
           && float.IsFinite(Mix);

    public readonly bool Equals(FlangerSettings other)
        => Rate.Equals(other.Rate) && Depth.Equals(other.Depth) && Delay.Equals(other.Delay) && Feedback.Equals(other.Feedback)
           && Spread.Equals(other.Spread) && Mix.Equals(other.Mix);

    public override readonly bool Equals(object? obj) => obj is FlangerSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Rate, Depth, Delay, Feedback, Spread, Mix);

    public static bool operator ==(FlangerSettings left, FlangerSettings right) => left.Equals(right);

    public static bool operator !=(FlangerSettings left, FlangerSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"{Rate} Hz, {Delay} to {Delay + Depth} ms, feedback {Feedback}, spread {Spread}, mix {Mix}";
}
