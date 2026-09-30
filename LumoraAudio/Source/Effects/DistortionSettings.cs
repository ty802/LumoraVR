// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A distortion as plain numbers: Drive is the gain into the curve and Level the gain after it, both in decibels;
// Tone is the corner of the low-pass on the distorted signal in hertz; Mix crossfades from the dry signal (0) to
// the distorted one alone (1). Clamped pulls anything outside the limits in, and a NaN falls back to that number's
// default.
public struct DistortionSettings : IEquatable<DistortionSettings>
{
    public const float MinDrive = 0f;
    public const float MaxDrive = 48f;
    public const float MinTone = 500f;
    public const float MaxTone = 20000f;
    public const float MinLevel = -24f;
    public const float MaxLevel = 12f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;

    public const DistortionCurve DefaultCurve = DistortionCurve.SoftClip;
    public const float DefaultDrive = 12f;
    public const float DefaultTone = 8000f;
    public const float DefaultLevel = 0f;
    public const float DefaultMix = 1f;
    public const DistortionOversampling DefaultOversampling = DistortionOversampling.Four;

    public DistortionCurve Curve;
    public float Drive;
    public float Tone;
    public float Level;
    public float Mix;
    public DistortionOversampling Oversampling;

    public static DistortionSettings Default => new()
    {
        Curve = DefaultCurve,
        Drive = DefaultDrive,
        Tone = DefaultTone,
        Level = DefaultLevel,
        Mix = DefaultMix,
        Oversampling = DefaultOversampling,
    };

    public readonly DistortionSettings Clamped() => new()
    {
        Curve = (uint)Curve <= (uint)DistortionCurve.Foldback ? Curve : DefaultCurve,
        Drive = DspMath.Limit(Drive, MinDrive, MaxDrive, DefaultDrive),
        Tone = DspMath.Limit(Tone, MinTone, MaxTone, DefaultTone),
        Level = DspMath.Limit(Level, MinLevel, MaxLevel, DefaultLevel),
        Mix = DspMath.Limit(Mix, MinMix, MaxMix, DefaultMix),
        Oversampling = (uint)Oversampling <= (uint)DistortionOversampling.Four ? Oversampling : DefaultOversampling,
    };

    public readonly bool IsFinite => float.IsFinite(Drive) && float.IsFinite(Tone) && float.IsFinite(Level) && float.IsFinite(Mix);

    public readonly bool Equals(DistortionSettings other)
        => Curve == other.Curve && Drive.Equals(other.Drive) && Tone.Equals(other.Tone) && Level.Equals(other.Level) && Mix.Equals(other.Mix)
           && Oversampling == other.Oversampling;

    public override readonly bool Equals(object? obj) => obj is DistortionSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Curve, Drive, Tone, Level, Mix, Oversampling);

    public static bool operator ==(DistortionSettings left, DistortionSettings right) => left.Equals(right);

    public static bool operator !=(DistortionSettings left, DistortionSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"{Curve}, drive {Drive} dB, tone {Tone} Hz, level {Level} dB, mix {Mix}, oversampling {Oversampling}";
}
