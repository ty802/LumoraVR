// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One room's reverb as plain numbers: what a reverb component keeps in its fields, what a script carries
// as one value, and what the filter glides toward. Milliseconds for the pre-delay, hertz for every
// frequency, seconds for the decay times, decibels for the levels. The limits are what the filter runs
// with: Clamped pulls anything outside them in, and a NaN falls back to that number's default.
public struct ReverbSettings : IEquatable<ReverbSettings>
{
    public const float MinPreDelay = 0f;
    public const float MaxPreDelay = 300f;
    public const float MinCrossover = 30f;
    public const float MaxCrossover = 3000f;
    public const float MinRT60 = 0.1f;
    public const float MaxRT60 = 30f;
    public const float MinHFDamping = 500f;
    public const float MaxHFDamping = 20000f;
    public const float MinEQFrequency = 30f;
    public const float MaxEQFrequency = 16000f;
    public const float MinEQLevel = -18f;
    public const float MaxEQLevel = 18f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;
    public const float MinLevel = -60f;
    public const float MaxLevel = 12f;

    // A medium room: about a second of decay, a little longer in the bass, a soft top end.
    public const float DefaultPreDelay = 12f;
    public const float DefaultCrossover = 250f;
    public const float DefaultLowRT60 = 1.3f;
    public const float DefaultMidRT60 = 1f;
    public const float DefaultHFDamping = 7000f;
    public const float DefaultEQ1Frequency = 250f;
    public const float DefaultEQ1Level = 0f;
    public const float DefaultEQ2Frequency = 3000f;
    public const float DefaultEQ2Level = 0f;
    public const float DefaultMix = 0.25f;
    public const float DefaultLevel = 0f;

    public float PreDelay;
    public float Crossover;

    // Time to fall 60 dB below the crossover and above it.
    public float LowRT60;
    public float MidRT60;

    // Where the decay time has dropped to half the mid one; above it the room keeps losing its top end.
    public float HFDamping;

    // Two peaking bands on the reverb return.
    public float EQ1Frequency;
    public float EQ1Level;
    public float EQ2Frequency;
    public float EQ2Level;

    // 0 is the dry signal alone, 1 the reverb alone, 0.5 both at full level.
    public float Mix;

    // Gain on the reverb return only, so it never touches the dry signal.
    public float Level;

    public static ReverbSettings Default => new()
    {
        PreDelay = DefaultPreDelay,
        Crossover = DefaultCrossover,
        LowRT60 = DefaultLowRT60,
        MidRT60 = DefaultMidRT60,
        HFDamping = DefaultHFDamping,
        EQ1Frequency = DefaultEQ1Frequency,
        EQ1Level = DefaultEQ1Level,
        EQ2Frequency = DefaultEQ2Frequency,
        EQ2Level = DefaultEQ2Level,
        Mix = DefaultMix,
        Level = DefaultLevel,
    };

    public readonly ReverbSettings Clamped() => new()
    {
        PreDelay = Limit(PreDelay, MinPreDelay, MaxPreDelay, DefaultPreDelay),
        Crossover = Limit(Crossover, MinCrossover, MaxCrossover, DefaultCrossover),
        LowRT60 = Limit(LowRT60, MinRT60, MaxRT60, DefaultLowRT60),
        MidRT60 = Limit(MidRT60, MinRT60, MaxRT60, DefaultMidRT60),
        HFDamping = Limit(HFDamping, MinHFDamping, MaxHFDamping, DefaultHFDamping),
        EQ1Frequency = Limit(EQ1Frequency, MinEQFrequency, MaxEQFrequency, DefaultEQ1Frequency),
        EQ1Level = Limit(EQ1Level, MinEQLevel, MaxEQLevel, DefaultEQ1Level),
        EQ2Frequency = Limit(EQ2Frequency, MinEQFrequency, MaxEQFrequency, DefaultEQ2Frequency),
        EQ2Level = Limit(EQ2Level, MinEQLevel, MaxEQLevel, DefaultEQ2Level),
        Mix = Limit(Mix, MinMix, MaxMix, DefaultMix),
        Level = Limit(Level, MinLevel, MaxLevel, DefaultLevel),
    };

    // True when every number is finite, which is what a script has to hand over before it may write one.
    public readonly bool IsFinite
        => float.IsFinite(PreDelay) && float.IsFinite(Crossover) && float.IsFinite(LowRT60) && float.IsFinite(MidRT60)
           && float.IsFinite(HFDamping) && float.IsFinite(EQ1Frequency) && float.IsFinite(EQ1Level)
           && float.IsFinite(EQ2Frequency) && float.IsFinite(EQ2Level) && float.IsFinite(Mix) && float.IsFinite(Level);

    public readonly bool Equals(ReverbSettings other)
        => PreDelay.Equals(other.PreDelay) && Crossover.Equals(other.Crossover) && LowRT60.Equals(other.LowRT60)
           && MidRT60.Equals(other.MidRT60) && HFDamping.Equals(other.HFDamping) && EQ1Frequency.Equals(other.EQ1Frequency)
           && EQ1Level.Equals(other.EQ1Level) && EQ2Frequency.Equals(other.EQ2Frequency) && EQ2Level.Equals(other.EQ2Level)
           && Mix.Equals(other.Mix) && Level.Equals(other.Level);

    public override readonly bool Equals(object? obj) => obj is ReverbSettings other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(HashCode.Combine(PreDelay, Crossover, LowRT60, MidRT60, HFDamping, EQ1Frequency),
            HashCode.Combine(EQ1Level, EQ2Frequency, EQ2Level, Mix, Level));

    public static bool operator ==(ReverbSettings left, ReverbSettings right) => left.Equals(right);

    public static bool operator !=(ReverbSettings left, ReverbSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"pre {PreDelay} ms, crossover {Crossover} Hz, RT60 {LowRT60}/{MidRT60} s, damping {HFDamping} Hz, "
           + $"EQ {EQ1Frequency} Hz {EQ1Level} dB / {EQ2Frequency} Hz {EQ2Level} dB, mix {Mix}, level {Level} dB";

    private static float Limit(float value, float min, float max, float fallback)
        => float.IsNaN(value) ? fallback : (value < min ? min : (value > max ? max : value));
}
