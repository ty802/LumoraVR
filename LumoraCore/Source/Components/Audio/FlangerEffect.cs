// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// The jet sweep: a copy of the sound between Delay and Delay + Depth milliseconds late, swept up and back Rate times
// a second, mixed back in so the comb it makes slides up and down the spectrum. Feedback (either sign) sharpens it,
// Spread turns the two sides' sweeps apart, and Mix crossfades from dry (0) through equal parts (0.5, the deepest
// notches) to the copy alone (1).
[ComponentCategory("Audio")]
public class FlangerEffect : AudioEffect
{
    [Range(FlangerSettings.MinRate, FlangerSettings.MaxRate, "0.00", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(FlangerSettings.MinDepth, FlangerSettings.MaxDepth, "0.00")]
    public readonly Sync<float> Depth;

    [Range(FlangerSettings.MinDelay, FlangerSettings.MaxDelay, "0.00")]
    public readonly Sync<float> Delay;

    [Range(FlangerSettings.MinFeedback, FlangerSettings.MaxFeedback, "0.00")]
    public readonly Sync<float> Feedback;

    [Range(FlangerSettings.MinSpread, FlangerSettings.MaxSpread, "0.00")]
    public readonly Sync<float> Spread;

    [Range(FlangerSettings.MinMix, FlangerSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public FlangerEffect()
    {
        Rate = new Sync<float>(this, FlangerSettings.DefaultRate);
        Depth = new Sync<float>(this, FlangerSettings.DefaultDepth);
        Delay = new Sync<float>(this, FlangerSettings.DefaultDelay);
        Feedback = new Sync<float>(this, FlangerSettings.DefaultFeedback);
        Spread = new Sync<float>(this, FlangerSettings.DefaultSpread);
        Mix = new Sync<float>(this, FlangerSettings.DefaultMix);
    }

    // The fields as stored; the filter clamps what it is given.
    public FlangerSettings Settings => new()
    {
        Rate = Rate.Value,
        Depth = Depth.Value,
        Delay = Delay.Value,
        Feedback = Feedback.Value,
        Spread = Spread.Value,
        Mix = Mix.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new FlangerFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((FlangerFilter)filter).Settings = Settings;
}
