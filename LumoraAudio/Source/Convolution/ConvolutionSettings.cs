// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// What a convolution reverb keeps besides its impulse response. Mix runs from the dry signal alone (0)
// through both at full level (0.5) to the reverb alone (1); Level is decibels on the reverb only;
// PreDelay is milliseconds before the reverb starts. Clamped pulls anything outside the limits in, and a
// NaN falls back to that number's default.
public struct ConvolutionSettings : IEquatable<ConvolutionSettings>
{
    public const float MinPreDelay = 0f;
    public const float MaxPreDelay = 500f;
    public const float MinMix = 0f;
    public const float MaxMix = 1f;
    public const float MinLevel = -60f;
    public const float MaxLevel = 12f;

    public const float DefaultPreDelay = 0f;
    public const float DefaultMix = 0.25f;
    public const float DefaultLevel = 0f;

    public float Mix;
    public float Level;
    public float PreDelay;

    public static ConvolutionSettings Default => new()
    {
        Mix = DefaultMix,
        Level = DefaultLevel,
        PreDelay = DefaultPreDelay,
    };

    public readonly ConvolutionSettings Clamped() => new()
    {
        Mix = Limit(Mix, MinMix, MaxMix, DefaultMix),
        Level = Limit(Level, MinLevel, MaxLevel, DefaultLevel),
        PreDelay = Limit(PreDelay, MinPreDelay, MaxPreDelay, DefaultPreDelay),
    };

    public readonly bool IsFinite => float.IsFinite(Mix) && float.IsFinite(Level) && float.IsFinite(PreDelay);

    public readonly bool Equals(ConvolutionSettings other)
        => Mix.Equals(other.Mix) && Level.Equals(other.Level) && PreDelay.Equals(other.PreDelay);

    public override readonly bool Equals(object? obj) => obj is ConvolutionSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Mix, Level, PreDelay);

    public static bool operator ==(ConvolutionSettings left, ConvolutionSettings right) => left.Equals(right);

    public static bool operator !=(ConvolutionSettings left, ConvolutionSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"mix {Mix}, level {Level} dB, pre-delay {PreDelay} ms";

    private static float Limit(float value, float min, float max, float fallback)
        => float.IsNaN(value) ? fallback : (value < min ? min : (value > max ? max : value));
}
