// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// An auto-pan as plain numbers: the balance swings Rate times a second on a sine, Depth of the way to each side
// (1 reaches hard left and hard right). Clamped pulls anything outside the limits in, and a NaN falls back to that
// number's default.
public struct AutoPanSettings : IEquatable<AutoPanSettings>
{
    public const float MinRate = 0.02f;
    public const float MaxRate = 20f;
    public const float MinDepth = 0f;
    public const float MaxDepth = 1f;

    public const float DefaultRate = 0.5f;
    public const float DefaultDepth = 1f;

    public float Rate;
    public float Depth;

    public static AutoPanSettings Default => new()
    {
        Rate = DefaultRate,
        Depth = DefaultDepth,
    };

    public readonly AutoPanSettings Clamped() => new()
    {
        Rate = DspMath.Limit(Rate, MinRate, MaxRate, DefaultRate),
        Depth = DspMath.Limit(Depth, MinDepth, MaxDepth, DefaultDepth),
    };

    public readonly bool IsFinite => float.IsFinite(Rate) && float.IsFinite(Depth);

    public readonly bool Equals(AutoPanSettings other) => Rate.Equals(other.Rate) && Depth.Equals(other.Depth);

    public override readonly bool Equals(object? obj) => obj is AutoPanSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Rate, Depth);

    public static bool operator ==(AutoPanSettings left, AutoPanSettings right) => left.Equals(right);

    public static bool operator !=(AutoPanSettings left, AutoPanSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"{Rate} Hz, depth {Depth}";
}
