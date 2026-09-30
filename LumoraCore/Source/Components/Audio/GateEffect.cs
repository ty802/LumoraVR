// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Shuts out what is quieter than the threshold: anything at or above it opens the gate, which stays open for Hold
// milliseconds after the last of it, then closes down by Range decibels (-90 is off, a few decibels is a gentle
// expander). Attack and release are time constants in milliseconds.
[ComponentCategory("Audio")]
public class GateEffect : AudioEffect
{
    [Range(GateSettings.MinThreshold, GateSettings.MaxThreshold, "0.0")]
    public readonly Sync<float> Threshold;

    [Range(GateSettings.MinRange, GateSettings.MaxRange, "0.0")]
    public readonly Sync<float> Range;

    [Range(GateSettings.MinAttack, GateSettings.MaxAttack, "0.00", exponential: true)]
    public readonly Sync<float> Attack;

    [Range(GateSettings.MinHold, GateSettings.MaxHold, "0")]
    public readonly Sync<float> Hold;

    [Range(GateSettings.MinRelease, GateSettings.MaxRelease, "0", exponential: true)]
    public readonly Sync<float> Release;

    public GateEffect()
    {
        Threshold = new Sync<float>(this, GateSettings.DefaultThreshold);
        Range = new Sync<float>(this, GateSettings.DefaultRange);
        Attack = new Sync<float>(this, GateSettings.DefaultAttack);
        Hold = new Sync<float>(this, GateSettings.DefaultHold);
        Release = new Sync<float>(this, GateSettings.DefaultRelease);
    }

    // The fields as stored; the filter clamps what it is given.
    public GateSettings Settings => new()
    {
        Threshold = Threshold.Value,
        Range = Range.Value,
        Attack = Attack.Value,
        Hold = Hold.Value,
        Release = Release.Value,
    };

    protected override IAudioDspFilter CreateFilter() => new GateFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((GateFilter)filter).Settings = Settings;
}
