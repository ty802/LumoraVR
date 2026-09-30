// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// A throbbing level: it swings Rate times a second between full and 1 - Depth. Spread turns the two sides' swings
// apart, up to opposite at 1, which throws the sound from side to side.
[ComponentCategory("Audio")]
public class TremoloEffect : AudioEffect
{
    [Range(TremoloSettings.MinRate, TremoloSettings.MaxRate, "0.00", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(TremoloSettings.MinDepth, TremoloSettings.MaxDepth, "0.00")]
    public readonly Sync<float> Depth;

    [Range(TremoloSettings.MinSpread, TremoloSettings.MaxSpread, "0.00")]
    public readonly Sync<float> Spread;

    public TremoloEffect()
    {
        Rate = new Sync<float>(this, TremoloSettings.DefaultRate);
        Depth = new Sync<float>(this, TremoloSettings.DefaultDepth);
        Spread = new Sync<float>(this, TremoloSettings.DefaultSpread);
    }

    // The fields as stored; the filter clamps what it is given.
    public TremoloSettings Settings => new()
    {
        Rate = Rate.Value,
        Depth = Depth.Value,
        Spread = Spread.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new TremoloFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((TremoloFilter)filter).Settings = Settings;
}
