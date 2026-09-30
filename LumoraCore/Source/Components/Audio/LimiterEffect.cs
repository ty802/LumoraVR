// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// A brickwall: nothing leaves above the ceiling (decibels below full scale), and a peak that would is ramped down
// over the 5 ms before it instead of being clipped, which is also the latency. Release is a time constant in
// milliseconds; InputGain pushes a quiet source up against the ceiling before it is limited.
[ComponentCategory("Audio")]
public class LimiterEffect : AudioEffect
{
    [Range(LimiterSettings.MinCeiling, LimiterSettings.MaxCeiling, "0.0")]
    public readonly Sync<float> Ceiling;

    [Range(LimiterSettings.MinRelease, LimiterSettings.MaxRelease, "0", exponential: true)]
    public readonly Sync<float> Release;

    [Range(LimiterSettings.MinInputGain, LimiterSettings.MaxInputGain, "0.0")]
    public readonly Sync<float> InputGain;

    public LimiterEffect()
    {
        Ceiling = new Sync<float>(this, LimiterSettings.DefaultCeiling);
        Release = new Sync<float>(this, LimiterSettings.DefaultRelease);
        InputGain = new Sync<float>(this, LimiterSettings.DefaultInputGain);
    }

    // The fields as stored; the filter clamps what it is given.
    public LimiterSettings Settings => new()
    {
        Ceiling = Ceiling.Value,
        Release = Release.Value,
        InputGain = InputGain.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new LimiterFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((LimiterFilter)filter).Settings = Settings;
}
