// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Evens out loud and quiet: above the threshold the level rises only 1/ratio as fast, with a soft corner Knee
// decibels wide. Attack and release are time constants in milliseconds; Makeup lifts the result back up. Lookahead
// runs the sound behind the detector so a transient is already under control when it arrives, at the cost of that
// much latency.
[ComponentCategory("Audio")]
public class CompressorEffect : AudioEffect
{
    [Range(CompressorSettings.MinThreshold, CompressorSettings.MaxThreshold, "0.0")]
    public readonly Sync<float> Threshold;

    [Range(CompressorSettings.MinRatio, CompressorSettings.MaxRatio, "0.0", exponential: true)]
    public readonly Sync<float> Ratio;

    [Range(CompressorSettings.MinKnee, CompressorSettings.MaxKnee, "0.0")]
    public readonly Sync<float> Knee;

    [Range(CompressorSettings.MinAttack, CompressorSettings.MaxAttack, "0.00", exponential: true)]
    public readonly Sync<float> Attack;

    [Range(CompressorSettings.MinRelease, CompressorSettings.MaxRelease, "0", exponential: true)]
    public readonly Sync<float> Release;

    [Range(CompressorSettings.MinMakeup, CompressorSettings.MaxMakeup, "0.0")]
    public readonly Sync<float> Makeup;

    public readonly Sync<CompressorDetector> Detector;

    [Range(CompressorSettings.MinLookahead, CompressorSettings.MaxLookahead, "0.0")]
    public readonly Sync<float> Lookahead;

    public CompressorEffect()
    {
        Threshold = new Sync<float>(this, CompressorSettings.DefaultThreshold);
        Ratio = new Sync<float>(this, CompressorSettings.DefaultRatio);
        Knee = new Sync<float>(this, CompressorSettings.DefaultKnee);
        Attack = new Sync<float>(this, CompressorSettings.DefaultAttack);
        Release = new Sync<float>(this, CompressorSettings.DefaultRelease);
        Makeup = new Sync<float>(this, CompressorSettings.DefaultMakeup);
        Detector = new Sync<CompressorDetector>(this, CompressorSettings.DefaultDetector);
        Lookahead = new Sync<float>(this, CompressorSettings.DefaultLookahead);
    }

    // The fields as stored; the filter clamps what it is given.
    public CompressorSettings Settings => new()
    {
        Threshold = Threshold.Value,
        Ratio = Ratio.Value,
        Knee = Knee.Value,
        Attack = Attack.Value,
        Release = Release.Value,
        Makeup = Makeup.Value,
        Detector = Detector.Value,
        Lookahead = Lookahead.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new CompressorFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((CompressorFilter)filter).Settings = Settings;
}
