// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A gate as plain numbers. Threshold and range are decibels: the gate opens on any sample at or above the
// threshold, and shut it turns the sound down by the range (-90 is as good as off, -10 a gentle expander).
// Attack and release are time constants in milliseconds; hold is how long it stays open after the last sample
// over the threshold. Clamped pulls anything outside the limits in, and a NaN falls back to that number's default.
public struct GateSettings : IEquatable<GateSettings>
{
    public const float MinThreshold = -80f;
    public const float MaxThreshold = 0f;
    public const float MinRange = -90f;
    public const float MaxRange = 0f;
    public const float MinAttack = 0.05f;
    public const float MaxAttack = 200f;
    public const float MinHold = 0f;
    public const float MaxHold = 2000f;
    public const float MinRelease = 1f;
    public const float MaxRelease = 5000f;

    public const float DefaultThreshold = -40f;
    public const float DefaultRange = -80f;
    public const float DefaultAttack = 1f;
    public const float DefaultHold = 50f;
    public const float DefaultRelease = 150f;

    public float Threshold;
    public float Range;
    public float Attack;
    public float Hold;
    public float Release;

    public static GateSettings Default => new()
    {
        Threshold = DefaultThreshold,
        Range = DefaultRange,
        Attack = DefaultAttack,
        Hold = DefaultHold,
        Release = DefaultRelease,
    };

    public readonly GateSettings Clamped() => new()
    {
        Threshold = DspMath.Limit(Threshold, MinThreshold, MaxThreshold, DefaultThreshold),
        Range = DspMath.Limit(Range, MinRange, MaxRange, DefaultRange),
        Attack = DspMath.Limit(Attack, MinAttack, MaxAttack, DefaultAttack),
        Hold = DspMath.Limit(Hold, MinHold, MaxHold, DefaultHold),
        Release = DspMath.Limit(Release, MinRelease, MaxRelease, DefaultRelease),
    };

    public readonly bool IsFinite
        => float.IsFinite(Threshold) && float.IsFinite(Range) && float.IsFinite(Attack) && float.IsFinite(Hold) && float.IsFinite(Release);

    public readonly bool Equals(GateSettings other)
        => Threshold.Equals(other.Threshold) && Range.Equals(other.Range) && Attack.Equals(other.Attack) && Hold.Equals(other.Hold)
           && Release.Equals(other.Release);

    public override readonly bool Equals(object? obj) => obj is GateSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Threshold, Range, Attack, Hold, Release);

    public static bool operator ==(GateSettings left, GateSettings right) => left.Equals(right);

    public static bool operator !=(GateSettings left, GateSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"threshold {Threshold} dB, range {Range} dB, attack {Attack} ms, hold {Hold} ms, release {Release} ms";
}
