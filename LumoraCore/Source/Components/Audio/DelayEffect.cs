// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// An echo: repeats Time milliseconds apart, each Feedback as loud as the one before and passed through a low-pass
// at HighCut and a high-pass at LowCut (hertz) on its way round. PingPong bounces the repeats from side to side.
// Mix runs from the dry signal alone (0) through both at full level (0.5) to the echoes alone (1). On an output the
// echoes keep coming for a while after its sound stops.
[ComponentCategory("Audio")]
public class DelayEffect : AudioEffect
{
    [Range(DelaySettings.MinTime, DelaySettings.MaxTime, "0", exponential: true)]
    public readonly Sync<float> Time;

    [Range(DelaySettings.MinFeedback, DelaySettings.MaxFeedback, "0.00")]
    public readonly Sync<float> Feedback;

    [Range(DelaySettings.MinHighCut, DelaySettings.MaxHighCut, "0", exponential: true)]
    public readonly Sync<float> HighCut;

    [Range(DelaySettings.MinLowCut, DelaySettings.MaxLowCut, "0", exponential: true)]
    public readonly Sync<float> LowCut;

    public readonly Sync<bool> PingPong;

    [Range(DelaySettings.MinMix, DelaySettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public DelayEffect()
    {
        Time = new Sync<float>(this, DelaySettings.DefaultTime);
        Feedback = new Sync<float>(this, DelaySettings.DefaultFeedback);
        HighCut = new Sync<float>(this, DelaySettings.DefaultHighCut);
        LowCut = new Sync<float>(this, DelaySettings.DefaultLowCut);
        PingPong = new Sync<bool>(this, DelaySettings.DefaultPingPong);
        Mix = new Sync<float>(this, DelaySettings.DefaultMix);
    }

    // The fields as stored; the filter clamps what it is given.
    public DelaySettings Settings => new()
    {
        Time = Time.Value,
        Feedback = Feedback.Value,
        HighCut = HighCut.Value,
        LowCut = LowCut.Value,
        PingPong = PingPong.Value,
        Mix = Mix.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new DelayFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((DelayFilter)filter).Settings = Settings;
}
