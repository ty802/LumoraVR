// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Narrows or widens the stereo image: 0 folds it to mono, 1 leaves it alone, 2 doubles the difference between the
// sides. Summed to mono the result is always what went in.
[ComponentCategory("Audio")]
public class StereoWidthEffect : AudioEffect
{
    [Range(StereoWidthSettings.MinWidth, StereoWidthSettings.MaxWidth, "0.00")]
    public readonly Sync<float> Width;

    public StereoWidthEffect()
    {
        Width = new Sync<float>(this, StereoWidthSettings.DefaultWidth);
    }

    // The field as stored; the filter clamps what it is given.
    public StereoWidthSettings Settings => new() { Width = Width.Value };

    protected override IAudioDspFilter CreateFilter() => new StereoWidthFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((StereoWidthFilter)filter).Settings = Settings;
}
