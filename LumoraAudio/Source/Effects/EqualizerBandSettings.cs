// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One band of an equalizer: a single cookbook section. Limits are the filter's (BiquadSettings).
public struct EqualizerBandSettings : IEquatable<EqualizerBandSettings>
{
    public BiquadFilterType Type;
    public float Frequency;
    public float Gain;
    public float Q;

    // Off passes the band's input untouched; switching crossfades.
    public bool Enabled;

    public static EqualizerBandSettings Default => new()
    {
        Type = BiquadFilterType.Peaking,
        Frequency = BiquadSettings.DefaultFrequency,
        Gain = BiquadSettings.DefaultGain,
        Q = BiquadSettings.DefaultQ,
        Enabled = true,
    };

    public static EqualizerBandSettings Make(BiquadFilterType type, float frequency, float gain, float q) => new()
    {
        Type = type,
        Frequency = frequency,
        Gain = gain,
        Q = q,
        Enabled = true,
    };

    public readonly EqualizerBandSettings Clamped() => new()
    {
        Type = BiquadSettings.ClampType(Type, BiquadFilterType.Peaking),
        Frequency = DspMath.Limit(Frequency, BiquadSettings.MinFrequency, BiquadSettings.MaxFrequency, BiquadSettings.DefaultFrequency),
        Gain = DspMath.Limit(Gain, BiquadSettings.MinGain, BiquadSettings.MaxGain, BiquadSettings.DefaultGain),
        Q = DspMath.Limit(Q, BiquadSettings.MinQ, BiquadSettings.MaxQ, BiquadSettings.DefaultQ),
        Enabled = Enabled,
    };

    public readonly bool IsFinite => float.IsFinite(Frequency) && float.IsFinite(Gain) && float.IsFinite(Q);

    public readonly bool Equals(EqualizerBandSettings other)
        => Type == other.Type && Frequency.Equals(other.Frequency) && Gain.Equals(other.Gain) && Q.Equals(other.Q) && Enabled == other.Enabled;

    public override readonly bool Equals(object? obj) => obj is EqualizerBandSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Type, Frequency, Gain, Q, Enabled);

    public static bool operator ==(EqualizerBandSettings left, EqualizerBandSettings right) => left.Equals(right);

    public static bool operator !=(EqualizerBandSettings left, EqualizerBandSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"{(Enabled ? "" : "off: ")}{Type} {Frequency} Hz {Gain} dB Q {Q}";
}
