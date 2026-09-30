// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// One filter: low-pass, high-pass, band-pass, notch, peaking, low or high shelf, or all-pass, with a low-pass or
// high-pass steepened to 24 or 48 dB per octave by Slope. On an output it colours that one sound (a radio, a wall
// between you and the band); on a listener, everything the listener hears. Frequency in hertz, gain in decibels
// (peaking and shelves only). Every change glides or crossfades, so it can be swept while it plays.
[ComponentCategory("Audio")]
public class FilterEffect : AudioEffect
{
    public readonly Sync<BiquadFilterType> Type;

    [Range(BiquadSettings.MinFrequency, BiquadSettings.MaxFrequency, "0", exponential: true)]
    public readonly Sync<float> Frequency;

    [Range(BiquadSettings.MinQ, BiquadSettings.MaxQ, "0.00", exponential: true)]
    public readonly Sync<float> Q;

    [Range(BiquadSettings.MinGain, BiquadSettings.MaxGain, "0.0")]
    public readonly Sync<float> Gain;

    public readonly Sync<FilterSlope> Slope;

    public FilterEffect()
    {
        Type = new Sync<BiquadFilterType>(this, BiquadSettings.DefaultType);
        Frequency = new Sync<float>(this, BiquadSettings.DefaultFrequency);
        Q = new Sync<float>(this, BiquadSettings.DefaultQ);
        Gain = new Sync<float>(this, BiquadSettings.DefaultGain);
        Slope = new Sync<FilterSlope>(this, BiquadSettings.DefaultSlope);
    }

    // The fields as stored; the filter clamps what it is given.
    public BiquadSettings Settings => new()
    {
        Type = Type.Value,
        Frequency = Frequency.Value,
        Q = Q.Value,
        Gain = Gain.Value,
        Slope = Slope.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new BiquadFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((BiquadFilter)filter).Settings = Settings;
}
