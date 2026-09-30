// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One filter as plain numbers: hertz for the corner, decibels for the gain. Q is the cookbook's; on a
// low-pass or high-pass 1/sqrt(2) is flat to the corner and higher values ring. Clamped pulls anything outside
// the limits in, and a NaN falls back to that number's default.
public struct BiquadSettings : IEquatable<BiquadSettings>
{
    public const float MinFrequency = 10f;
    public const float MaxFrequency = 22000f;
    public const float MinQ = 0.1f;
    public const float MaxQ = 20f;
    public const float MinGain = -24f;
    public const float MaxGain = 24f;

    public const BiquadFilterType DefaultType = BiquadFilterType.LowPass;
    public const float DefaultFrequency = 1000f;
    public const float DefaultQ = 0.70710677f;
    public const float DefaultGain = 0f;
    public const FilterSlope DefaultSlope = FilterSlope.Slope12;

    public BiquadFilterType Type;
    public float Frequency;
    public float Q;

    // Peaking and shelves only.
    public float Gain;

    public FilterSlope Slope;

    public static BiquadSettings Default => new()
    {
        Type = DefaultType,
        Frequency = DefaultFrequency,
        Q = DefaultQ,
        Gain = DefaultGain,
        Slope = DefaultSlope,
    };

    public readonly BiquadSettings Clamped() => new()
    {
        Type = ClampType(Type, DefaultType),
        Frequency = DspMath.Limit(Frequency, MinFrequency, MaxFrequency, DefaultFrequency),
        Q = DspMath.Limit(Q, MinQ, MaxQ, DefaultQ),
        Gain = DspMath.Limit(Gain, MinGain, MaxGain, DefaultGain),
        Slope = (uint)Slope <= (uint)FilterSlope.Slope48 ? Slope : DefaultSlope,
    };

    // Sections a slope takes for this type.
    public readonly int Stages => StagesFor(Type, Slope);

    public readonly bool IsFinite => float.IsFinite(Frequency) && float.IsFinite(Q) && float.IsFinite(Gain);

    public readonly bool Equals(BiquadSettings other)
        => Type == other.Type && Frequency.Equals(other.Frequency) && Q.Equals(other.Q) && Gain.Equals(other.Gain) && Slope == other.Slope;

    public override readonly bool Equals(object? obj) => obj is BiquadSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Type, Frequency, Q, Gain, Slope);

    public static bool operator ==(BiquadSettings left, BiquadSettings right) => left.Equals(right);

    public static bool operator !=(BiquadSettings left, BiquadSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"{Type} {Frequency} Hz, Q {Q}, {Gain} dB, {Slope}";

    public static int StagesFor(BiquadFilterType type, FilterSlope slope)
    {
        if (type == BiquadFilterType.Peaking || type == BiquadFilterType.LowShelf || type == BiquadFilterType.HighShelf)
            return 1;
        return slope switch
        {
            FilterSlope.Slope24 => 2,
            FilterSlope.Slope48 => 4,
            _ => 1,
        };
    }

    internal static BiquadFilterType ClampType(BiquadFilterType type, BiquadFilterType fallback)
        => (uint)type <= (uint)BiquadFilterType.AllPass ? type : fallback;
}
