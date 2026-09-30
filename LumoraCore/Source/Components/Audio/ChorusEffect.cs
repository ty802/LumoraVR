// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Thickens and widens: a copy of the sound Delay milliseconds late, the lateness swinging Depth milliseconds either
// way Rate times a second, mixed back in. Spread turns the two sides' swings apart (1 is opposite), Feedback feeds
// some of the copy back round, and Mix crossfades from dry (0) through equal parts (0.5) to the copy alone (1).
[ComponentCategory("Audio")]
public class ChorusEffect : AudioEffect
{
    [Range(ChorusSettings.MinRate, ChorusSettings.MaxRate, "0.00", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(ChorusSettings.MinDepth, ChorusSettings.MaxDepth, "0.00")]
    public readonly Sync<float> Depth;

    [Range(ChorusSettings.MinDelay, ChorusSettings.MaxDelay, "0.0")]
    public readonly Sync<float> Delay;

    [Range(ChorusSettings.MinFeedback, ChorusSettings.MaxFeedback, "0.00")]
    public readonly Sync<float> Feedback;

    [Range(ChorusSettings.MinSpread, ChorusSettings.MaxSpread, "0.00")]
    public readonly Sync<float> Spread;

    [Range(ChorusSettings.MinMix, ChorusSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public ChorusEffect()
    {
        Rate = new Sync<float>(this, ChorusSettings.DefaultRate);
        Depth = new Sync<float>(this, ChorusSettings.DefaultDepth);
        Delay = new Sync<float>(this, ChorusSettings.DefaultDelay);
        Feedback = new Sync<float>(this, ChorusSettings.DefaultFeedback);
        Spread = new Sync<float>(this, ChorusSettings.DefaultSpread);
        Mix = new Sync<float>(this, ChorusSettings.DefaultMix);
    }

    // The fields as stored; the filter clamps what it is given.
    public ChorusSettings Settings => new()
    {
        Rate = Rate.Value,
        Depth = Depth.Value,
        Delay = Delay.Value,
        Feedback = Feedback.Value,
        Spread = Spread.Value,
        Mix = Mix.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new ChorusFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((ChorusFilter)filter).Settings = Settings;
}
