// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A phaser as plain numbers: the all-pass corners sweep between MinFrequency and MaxFrequency (hertz, evenly in
// octaves) Rate times a second. Every two stages make one notch, Feedback (either sign) sharpens them, Spread
// moves the right side's sweep up to half a cycle along, and Mix crossfades from the dry signal (0) through equal
// parts (0.5, where the notches go all the way down) to the phase-shifted copy alone (1). Clamped pulls anything
// outside the limits in, and a NaN falls back to that number's default.
public struct PhaserSettings : IEquatable<PhaserSettings>
{
    public const float MinRate = 0.01f;
    public const float MaxRate = 10f;
    public const float MinSweepFrequency = 20f;
    public const float MaxSweepFrequency = 20000f;
    public const int MinStages = 1;
    public const int MaxStages = 12;
    public const float MinFeedback = -0.95f;
    public const float MaxFeedback = 0.95f;
    public const float MinSpread = 0f;
    public const float MaxSpread = 1f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;

    public const float DefaultRate = 0.4f;
    public const float DefaultMinFrequency = 300f;
    public const float DefaultMaxFrequency = 3000f;
    public const int DefaultStages = 6;
    public const float DefaultFeedback = 0.4f;
    public const float DefaultSpread = 0.5f;
    public const float DefaultMix = 0.5f;

    public float Rate;
    public float MinFrequency;
    public float MaxFrequency;
    public int Stages;
    public float Feedback;
    public float Spread;
    public float Mix;

    public static PhaserSettings Default => new()
    {
        Rate = DefaultRate,
        MinFrequency = DefaultMinFrequency,
        MaxFrequency = DefaultMaxFrequency,
        Stages = DefaultStages,
        Feedback = DefaultFeedback,
        Spread = DefaultSpread,
        Mix = DefaultMix,
    };

    public readonly PhaserSettings Clamped() => new()
    {
        Rate = DspMath.Limit(Rate, MinRate, MaxRate, DefaultRate),
        MinFrequency = DspMath.Limit(MinFrequency, MinSweepFrequency, MaxSweepFrequency, DefaultMinFrequency),
        MaxFrequency = DspMath.Limit(MaxFrequency, MinSweepFrequency, MaxSweepFrequency, DefaultMaxFrequency),
        Stages = Math.Clamp(Stages, MinStages, MaxStages),
        Feedback = DspMath.Limit(Feedback, MinFeedback, MaxFeedback, DefaultFeedback),
        Spread = DspMath.Limit(Spread, MinSpread, MaxSpread, DefaultSpread),
        Mix = DspMath.Limit(Mix, MinMix, MaxMix, DefaultMix),
    };

    public readonly bool IsFinite
        => float.IsFinite(Rate) && float.IsFinite(MinFrequency) && float.IsFinite(MaxFrequency) && float.IsFinite(Feedback)
           && float.IsFinite(Spread) && float.IsFinite(Mix);

    public readonly bool Equals(PhaserSettings other)
        => Rate.Equals(other.Rate) && MinFrequency.Equals(other.MinFrequency) && MaxFrequency.Equals(other.MaxFrequency) && Stages == other.Stages
           && Feedback.Equals(other.Feedback) && Spread.Equals(other.Spread) && Mix.Equals(other.Mix);

    public override readonly bool Equals(object? obj) => obj is PhaserSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Rate, MinFrequency, MaxFrequency, Stages, Feedback, Spread, Mix);

    public static bool operator ==(PhaserSettings left, PhaserSettings right) => left.Equals(right);

    public static bool operator !=(PhaserSettings left, PhaserSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"{Rate} Hz, {MinFrequency} to {MaxFrequency} Hz, {Stages} stages, feedback {Feedback}, spread {Spread}, mix {Mix}";
}
