// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Stereo width as one number: the side signal is scaled by it, so 0 is mono, 1 leaves the image as it is and 2
// doubles the difference between the sides. Clamped pulls it into range, and a NaN falls back to the default.
public struct StereoWidthSettings : IEquatable<StereoWidthSettings>
{
    public const float MinWidth = 0f;
    public const float MaxWidth = 2f;
    public const float DefaultWidth = 1f;

    public float Width;

    public static StereoWidthSettings Default => new() { Width = DefaultWidth };

    public readonly StereoWidthSettings Clamped() => new() { Width = DspMath.Limit(Width, MinWidth, MaxWidth, DefaultWidth) };

    public readonly bool IsFinite => float.IsFinite(Width);

    public readonly bool Equals(StereoWidthSettings other) => Width.Equals(other.Width);

    public override readonly bool Equals(object? obj) => obj is StereoWidthSettings other && Equals(other);

    public override readonly int GetHashCode() => Width.GetHashCode();

    public static bool operator ==(StereoWidthSettings left, StereoWidthSettings right) => left.Equals(right);

    public static bool operator !=(StereoWidthSettings left, StereoWidthSettings right) => !left.Equals(right);

    public override readonly string ToString() => $"width {Width}";
}
