// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Grit: Drive decibels into a curve (soft clip, hard clip, an asymmetric tube-like one, or foldback), a low-pass at
// Tone hertz to take the fizz off, then Level decibels. Mix crossfades from dry (0) to fully distorted (1). The
// curve runs oversampled so the harmonics it makes do not fold back down as noise; None turns that off for the
// lo-fi sound it has. Always 42 samples of latency, whatever the oversampling.
[ComponentCategory("Audio")]
public class DistortionEffect : AudioEffect
{
    public readonly Sync<DistortionCurve> Curve;

    [Range(DistortionSettings.MinDrive, DistortionSettings.MaxDrive, "0.0")]
    public readonly Sync<float> Drive;

    [Range(DistortionSettings.MinTone, DistortionSettings.MaxTone, "0", exponential: true)]
    public readonly Sync<float> Tone;

    [Range(DistortionSettings.MinLevel, DistortionSettings.MaxLevel, "0.0")]
    public readonly Sync<float> Level;

    [Range(DistortionSettings.MinMix, DistortionSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public readonly Sync<DistortionOversampling> Oversampling;

    public DistortionEffect()
    {
        Curve = new Sync<DistortionCurve>(this, DistortionSettings.DefaultCurve);
        Drive = new Sync<float>(this, DistortionSettings.DefaultDrive);
        Tone = new Sync<float>(this, DistortionSettings.DefaultTone);
        Level = new Sync<float>(this, DistortionSettings.DefaultLevel);
        Mix = new Sync<float>(this, DistortionSettings.DefaultMix);
        Oversampling = new Sync<DistortionOversampling>(this, DistortionSettings.DefaultOversampling);
    }

    // The fields as stored; the filter clamps what it is given.
    public DistortionSettings Settings => new()
    {
        Curve = Curve.Value,
        Drive = Drive.Value,
        Tone = Tone.Value,
        Level = Level.Value,
        Mix = Mix.Value,
        Oversampling = Oversampling.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new DistortionFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((DistortionFilter)filter).Settings = Settings;
}
