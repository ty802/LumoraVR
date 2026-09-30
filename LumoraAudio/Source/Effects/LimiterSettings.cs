// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A limiter as plain numbers: the ceiling in decibels below full scale, the release in milliseconds (a time
// constant), and a gain in decibels applied before the limiting, to push a quiet source up against the ceiling.
// Clamped pulls anything outside the limits in, and a NaN falls back to that number's default.
public struct LimiterSettings : IEquatable<LimiterSettings>
{
    public const float MinCeiling = -30f;
    public const float MaxCeiling = 0f;
    public const float MinRelease = 1f;
    public const float MaxRelease = 1000f;
    public const float MinInputGain = -12f;
    public const float MaxInputGain = 24f;

    public const float DefaultCeiling = -1f;
    public const float DefaultRelease = 50f;
    public const float DefaultInputGain = 0f;

    public float Ceiling;
    public float Release;
    public float InputGain;

    public static LimiterSettings Default => new()
    {
        Ceiling = DefaultCeiling,
        Release = DefaultRelease,
        InputGain = DefaultInputGain,
    };

    public readonly LimiterSettings Clamped() => new()
    {
        Ceiling = DspMath.Limit(Ceiling, MinCeiling, MaxCeiling, DefaultCeiling),
        Release = DspMath.Limit(Release, MinRelease, MaxRelease, DefaultRelease),
        InputGain = DspMath.Limit(InputGain, MinInputGain, MaxInputGain, DefaultInputGain),
    };

    public readonly bool IsFinite => float.IsFinite(Ceiling) && float.IsFinite(Release) && float.IsFinite(InputGain);

    public readonly bool Equals(LimiterSettings other)
        => Ceiling.Equals(other.Ceiling) && Release.Equals(other.Release) && InputGain.Equals(other.InputGain);

    public override readonly bool Equals(object? obj) => obj is LimiterSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Ceiling, Release, InputGain);

    public static bool operator ==(LimiterSettings left, LimiterSettings right) => left.Equals(right);

    public static bool operator !=(LimiterSettings left, LimiterSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"ceiling {Ceiling} dB, release {Release} ms, input {InputGain} dB";
}
