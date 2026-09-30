// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A tremolo as plain numbers: the level swings Rate times a second between full and 1 - Depth, on a sine. Spread
// moves the right side's swing up to half a cycle along (1 is opposite, which throws the sound from side to side).
// Clamped pulls anything outside the limits in, and a NaN falls back to that number's default.
public struct TremoloSettings : IEquatable<TremoloSettings>
{
    public const float MinRate = 0.05f;
    public const float MaxRate = 30f;
    public const float MinDepth = 0f;
    public const float MaxDepth = 1f;
    public const float MinSpread = 0f;
    public const float MaxSpread = 1f;

    public const float DefaultRate = 5f;
    public const float DefaultDepth = 0.5f;
    public const float DefaultSpread = 0f;

    public float Rate;
    public float Depth;
    public float Spread;

    public static TremoloSettings Default => new()
    {
        Rate = DefaultRate,
        Depth = DefaultDepth,
        Spread = DefaultSpread,
    };

    public readonly TremoloSettings Clamped() => new()
    {
        Rate = DspMath.Limit(Rate, MinRate, MaxRate, DefaultRate),
        Depth = DspMath.Limit(Depth, MinDepth, MaxDepth, DefaultDepth),
        Spread = DspMath.Limit(Spread, MinSpread, MaxSpread, DefaultSpread),
    };

    public readonly bool IsFinite => float.IsFinite(Rate) && float.IsFinite(Depth) && float.IsFinite(Spread);

    public readonly bool Equals(TremoloSettings other) => Rate.Equals(other.Rate) && Depth.Equals(other.Depth) && Spread.Equals(other.Spread);

    public override readonly bool Equals(object? obj) => obj is TremoloSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Rate, Depth, Spread);

    public static bool operator ==(TremoloSettings left, TremoloSettings right) => left.Equals(right);

    public static bool operator !=(TremoloSettings left, TremoloSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"{Rate} Hz, depth {Depth}, spread {Spread}";
}
