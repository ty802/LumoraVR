// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// The swirl: a chain of all-pass stages whose corners sweep between MinFrequency and MaxFrequency (hertz) Rate
// times a second, mixed against the dry sound so notches slide through the spectrum, one for every two stages.
// Feedback (either sign) deepens them, Spread turns the two sides' sweeps apart, and Mix crossfades from dry (0)
// through equal parts (0.5, the deepest notches) to the phase-shifted copy alone (1).
[ComponentCategory("Audio")]
public class PhaserEffect : AudioEffect
{
    [Range(PhaserSettings.MinRate, PhaserSettings.MaxRate, "0.00", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(PhaserSettings.MinSweepFrequency, PhaserSettings.MaxSweepFrequency, "0", exponential: true)]
    public readonly Sync<float> MinFrequency;

    [Range(PhaserSettings.MinSweepFrequency, PhaserSettings.MaxSweepFrequency, "0", exponential: true)]
    public readonly Sync<float> MaxFrequency;

    [Range(PhaserSettings.MinStages, PhaserSettings.MaxStages)]
    public readonly Sync<int> Stages;

    [Range(PhaserSettings.MinFeedback, PhaserSettings.MaxFeedback, "0.00")]
    public readonly Sync<float> Feedback;

    [Range(PhaserSettings.MinSpread, PhaserSettings.MaxSpread, "0.00")]
    public readonly Sync<float> Spread;

    [Range(PhaserSettings.MinMix, PhaserSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public PhaserEffect()
    {
        Rate = new Sync<float>(this, PhaserSettings.DefaultRate);
        MinFrequency = new Sync<float>(this, PhaserSettings.DefaultMinFrequency);
        MaxFrequency = new Sync<float>(this, PhaserSettings.DefaultMaxFrequency);
        Stages = new Sync<int>(this, PhaserSettings.DefaultStages);
        Feedback = new Sync<float>(this, PhaserSettings.DefaultFeedback);
        Spread = new Sync<float>(this, PhaserSettings.DefaultSpread);
        Mix = new Sync<float>(this, PhaserSettings.DefaultMix);
    }

    // The fields as stored; the filter clamps what it is given.
    public PhaserSettings Settings => new()
    {
        Rate = Rate.Value,
        MinFrequency = MinFrequency.Value,
        MaxFrequency = MaxFrequency.Value,
        Stages = Stages.Value,
        Feedback = Feedback.Value,
        Spread = Spread.Value,
        Mix = Mix.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new PhaserFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((PhaserFilter)filter).Settings = Settings;
}
