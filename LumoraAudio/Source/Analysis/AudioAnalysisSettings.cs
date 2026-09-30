// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// How an AudioAnalysis cuts up its spectrum. FftSize is larger for finer frequency detail and slower
// response; frequencies are hertz; Smoothing is the share of a band's level kept each sixtieth of a second,
// 0 following every frame and 0.99 barely moving. Clamped pulls anything out of range in, a NaN falls back
// to the default, and FftSize rounds up to the next length made of twos, threes and fives: those are the
// fastest, and every length gets a transform plan kept for the life of the process, so a field anyone can
// write must not be able to ask for sixteen thousand of them.
public struct AudioAnalysisSettings : IEquatable<AudioAnalysisSettings>
{
    public const int MinFftSize = 64;
    public const int MaxFftSize = 16384;
    public const int MinBands = 1;
    public const int MaxBands = 128;
    public const float MinFrequencyLimit = 10f;
    public const float MaxFrequencyLimit = 24000f;
    public const float MaxSmoothing = 0.99f;

    public const int DefaultFftSize = 2048;
    public const int DefaultBandCount = 16;
    public const float DefaultMinFrequency = 30f;
    public const float DefaultMaxFrequency = 16000f;
    public const float DefaultSmoothing = 0.5f;

    public int FftSize;
    public int BandCount;
    public FrequencyScale BandScale;
    public WindowShape Window;
    public float MinFrequency;
    public float MaxFrequency;
    public float Smoothing;

    public static AudioAnalysisSettings Default => new()
    {
        FftSize = DefaultFftSize,
        BandCount = DefaultBandCount,
        BandScale = FrequencyScale.Logarithmic,
        Window = WindowShape.Hann,
        MinFrequency = DefaultMinFrequency,
        MaxFrequency = DefaultMaxFrequency,
        Smoothing = DefaultSmoothing,
    };

    public readonly AudioAnalysisSettings Clamped()
    {
        float min = float.IsNaN(MinFrequency) ? DefaultMinFrequency : Math.Clamp(MinFrequency, MinFrequencyLimit, MaxFrequencyLimit);
        float max = float.IsNaN(MaxFrequency) ? DefaultMaxFrequency : Math.Clamp(MaxFrequency, MinFrequencyLimit, MaxFrequencyLimit);
        if (max <= min)
            max = Math.Min(MaxFrequencyLimit, min * 2f);
        return new AudioAnalysisSettings
        {
            FftSize = FftSizes.NextFast(Math.Clamp(FftSize, MinFftSize, MaxFftSize)),
            BandCount = Math.Clamp(BandCount, MinBands, MaxBands),
            BandScale = Enum.IsDefined(BandScale) ? BandScale : FrequencyScale.Logarithmic,
            Window = Enum.IsDefined(Window) ? Window : WindowShape.Hann,
            MinFrequency = min,
            MaxFrequency = max,
            Smoothing = float.IsNaN(Smoothing) ? DefaultSmoothing : Math.Clamp(Smoothing, 0f, MaxSmoothing),
        };
    }

    public readonly bool Equals(AudioAnalysisSettings other)
        => FftSize == other.FftSize && BandCount == other.BandCount && BandScale == other.BandScale && Window == other.Window
           && MinFrequency.Equals(other.MinFrequency) && MaxFrequency.Equals(other.MaxFrequency) && Smoothing.Equals(other.Smoothing);

    public override readonly bool Equals(object? obj) => obj is AudioAnalysisSettings other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(FftSize, BandCount, BandScale, Window, MinFrequency, MaxFrequency, Smoothing);

    public static bool operator ==(AudioAnalysisSettings left, AudioAnalysisSettings right) => left.Equals(right);

    public static bool operator !=(AudioAnalysisSettings left, AudioAnalysisSettings right) => !left.Equals(right);
}
