// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Swings the balance from side to side Rate times a second, Depth of the way to each side (1 reaches hard left and
// hard right), at constant power so the sound moves without pumping. This is on top of where the output sits in
// the room.
[ComponentCategory("Audio")]
public class AutoPanEffect : AudioEffect
{
    [Range(AutoPanSettings.MinRate, AutoPanSettings.MaxRate, "0.00", exponential: true)]
    public readonly Sync<float> Rate;

    [Range(AutoPanSettings.MinDepth, AutoPanSettings.MaxDepth, "0.00")]
    public readonly Sync<float> Depth;

    public AutoPanEffect()
    {
        Rate = new Sync<float>(this, AutoPanSettings.DefaultRate);
        Depth = new Sync<float>(this, AutoPanSettings.DefaultDepth);
    }

    // The fields as stored; the filter clamps what it is given.
    public AutoPanSettings Settings => new()
    {
        Rate = Rate.Value,
        Depth = Depth.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new AutoPanFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((AutoPanFilter)filter).Settings = Settings;
}
