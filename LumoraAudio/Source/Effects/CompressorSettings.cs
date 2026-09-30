// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A compressor as plain numbers: decibels for threshold, knee and makeup, milliseconds for the times. Attack and
// release are time constants: the gain covers 1 - 1/e of a change in that long. Clamped pulls anything outside
// the limits in, and a NaN falls back to that number's default.
public struct CompressorSettings : IEquatable<CompressorSettings>
{
    public const float MinThreshold = -60f;
    public const float MaxThreshold = 0f;
    public const float MinRatio = 1f;
    public const float MaxRatio = 50f;
    public const float MinKnee = 0f;
    public const float MaxKnee = 24f;
    public const float MinAttack = 0.05f;
    public const float MaxAttack = 500f;
    public const float MinRelease = 5f;
    public const float MaxRelease = 5000f;
    public const float MinMakeup = -24f;
    public const float MaxMakeup = 24f;
    public const float MinLookahead = 0f;
    public const float MaxLookahead = 10f;

    public const float DefaultThreshold = -18f;
    public const float DefaultRatio = 4f;
    public const float DefaultKnee = 6f;
    public const float DefaultAttack = 10f;
    public const float DefaultRelease = 120f;
    public const float DefaultMakeup = 0f;
    public const CompressorDetector DefaultDetector = CompressorDetector.Peak;
    public const float DefaultLookahead = 0f;

    public float Threshold;
    public float Ratio;

    // Width of the soft corner around the threshold; 0 is a hard knee.
    public float Knee;

    public float Attack;
    public float Release;
    public float Makeup;
    public CompressorDetector Detector;

    // Delays the sound behind the detector so the gain is already down when a transient arrives. Adds exactly
    // this much latency (CompressorFilter.LatencySamples).
    public float Lookahead;

    public static CompressorSettings Default => new()
    {
        Threshold = DefaultThreshold,
        Ratio = DefaultRatio,
        Knee = DefaultKnee,
        Attack = DefaultAttack,
        Release = DefaultRelease,
        Makeup = DefaultMakeup,
        Detector = DefaultDetector,
        Lookahead = DefaultLookahead,
    };

    public readonly CompressorSettings Clamped() => new()
    {
        Threshold = DspMath.Limit(Threshold, MinThreshold, MaxThreshold, DefaultThreshold),
        Ratio = DspMath.Limit(Ratio, MinRatio, MaxRatio, DefaultRatio),
        Knee = DspMath.Limit(Knee, MinKnee, MaxKnee, DefaultKnee),
        Attack = DspMath.Limit(Attack, MinAttack, MaxAttack, DefaultAttack),
        Release = DspMath.Limit(Release, MinRelease, MaxRelease, DefaultRelease),
        Makeup = DspMath.Limit(Makeup, MinMakeup, MaxMakeup, DefaultMakeup),
        Detector = (uint)Detector <= (uint)CompressorDetector.Rms ? Detector : DefaultDetector,
        Lookahead = DspMath.Limit(Lookahead, MinLookahead, MaxLookahead, DefaultLookahead),
    };

    public readonly bool IsFinite
        => float.IsFinite(Threshold) && float.IsFinite(Ratio) && float.IsFinite(Knee) && float.IsFinite(Attack)
           && float.IsFinite(Release) && float.IsFinite(Makeup) && float.IsFinite(Lookahead);

    // Decibels of gain the static curve applies to a level in decibels, makeup left out: 0 below the knee,
    // a quadratic through the knee, (1/ratio - 1) of the overshoot above it.
    public readonly float CurveAt(float levelDb)
    {
        float slope = 1f / Ratio - 1f;
        float over = levelDb - Threshold;
        if (Knee > 0f && 2f * over > -Knee && 2f * over < Knee)
        {
            float into = over + 0.5f * Knee;
            return slope * into * into / (2f * Knee);
        }
        return over > 0f ? slope * over : 0f;
    }

    public readonly bool Equals(CompressorSettings other)
        => Threshold.Equals(other.Threshold) && Ratio.Equals(other.Ratio) && Knee.Equals(other.Knee) && Attack.Equals(other.Attack)
           && Release.Equals(other.Release) && Makeup.Equals(other.Makeup) && Detector == other.Detector && Lookahead.Equals(other.Lookahead);

    public override readonly bool Equals(object? obj) => obj is CompressorSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Threshold, Ratio, Knee, Attack, Release, Makeup, Detector, Lookahead);

    public static bool operator ==(CompressorSettings left, CompressorSettings right) => left.Equals(right);

    public static bool operator !=(CompressorSettings left, CompressorSettings right) => !left.Equals(right);

    public override readonly string ToString()
        => $"threshold {Threshold} dB, ratio {Ratio}, knee {Knee} dB, attack {Attack} ms, release {Release} ms, makeup {Makeup} dB, {Detector}, lookahead {Lookahead} ms";
}
