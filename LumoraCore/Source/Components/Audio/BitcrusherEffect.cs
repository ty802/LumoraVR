// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Lo-fi: every sample rounded to Bits of resolution and held at a Rate of that many hertz, aliasing and all. Mix
// crossfades from dry (0) to fully crushed (1).
[ComponentCategory("Audio")]
public class BitcrusherEffect : AudioEffect
{
    [Range(BitcrusherSettings.MinBits, BitcrusherSettings.MaxBits, "0.0")]
    public readonly Sync<float> Bits;

    [Range(BitcrusherSettings.MinRate, BitcrusherSettings.MaxRate, "0", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(BitcrusherSettings.MinMix, BitcrusherSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    public BitcrusherEffect()
    {
        Bits = new Sync<float>(this, BitcrusherSettings.DefaultBits);
        Rate = new Sync<float>(this, BitcrusherSettings.DefaultRate);
        Mix = new Sync<float>(this, BitcrusherSettings.DefaultMix);
    }

    // The fields as stored; the filter clamps what it is given.
    public BitcrusherSettings Settings => new()
    {
        Bits = Bits.Value,
        Rate = Rate.Value,
        Mix = Mix.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new BitcrusherFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((BitcrusherFilter)filter).Settings = Settings;
}
