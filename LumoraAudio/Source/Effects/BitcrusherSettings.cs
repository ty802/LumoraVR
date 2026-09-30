// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A bitcrusher as plain numbers: Bits of resolution across full scale (fractions allowed, so it can glide), the
// rate in hertz a sample is taken and held at, and Mix crossfading from the dry signal (0) to the crushed one alone
// (1). A rate at or above the mixer's takes every sample. Clamped pulls anything outside the limits in, and a NaN
// falls back to that number's default.
public struct BitcrusherSettings : IEquatable<BitcrusherSettings>
{
    public const float MinBits = 1f;
    public const float MaxBits = 24f;
    public const float MinRate = 100f;
    public const float MaxRate = 48000f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;

    public const float DefaultBits = 8f;
    public const float DefaultRate = 8000f;
    public const float DefaultMix = 1f;

    public float Bits;
    public float Rate;
    public float Mix;

    public static BitcrusherSettings Default => new()
    {
        Bits = DefaultBits,
        Rate = DefaultRate,
        Mix = DefaultMix,
    };

    public readonly BitcrusherSettings Clamped() => new()
    {
        Bits = DspMath.Limit(Bits, MinBits, MaxBits, DefaultBits),
        Rate = DspMath.Limit(Rate, MinRate, MaxRate, DefaultRate),
        Mix = DspMath.Limit(Mix, MinMix, MaxMix, DefaultMix),
    };

    public readonly bool IsFinite => float.IsFinite(Bits) && float.IsFinite(Rate) && float.IsFinite(Mix);

    public readonly bool Equals(BitcrusherSettings other) => Bits.Equals(other.Bits) && Rate.Equals(other.Rate) && Mix.Equals(other.Mix);

    public override readonly bool Equals(object? obj) => obj is BitcrusherSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Bits, Rate, Mix);

    public static bool operator ==(BitcrusherSettings left, BitcrusherSettings right) => left.Equals(right);

    public static bool operator !=(BitcrusherSettings left, BitcrusherSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"{Bits} bits, {Rate} Hz, mix {Mix}";
}
