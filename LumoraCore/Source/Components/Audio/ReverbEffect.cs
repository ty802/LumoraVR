// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// A room carried by a listener: everything that listener hears goes through it, apart from sounds that
// skip effects (UI, a voice monitor). For a room people walk in and out of, see ReverbZone. Pre-delay is
// in milliseconds, frequencies in hertz, decay times in seconds, levels in decibels; Mix runs from the dry
// signal alone (0) through both at full level (0.5) to the reverb alone (1).
[ComponentCategory("Audio")]
public class ReverbEffect : AudioEffect
{
    [Range(ReverbSettings.MinPreDelay, ReverbSettings.MaxPreDelay, "0")]
    public readonly Sync<float> PreDelay;

    [Range(ReverbSettings.MinCrossover, ReverbSettings.MaxCrossover, "0", exponential: true)]
    public readonly Sync<float> Crossover;

    [Range(ReverbSettings.MinRT60, ReverbSettings.MaxRT60, "0.00", exponential: true)]
    public readonly Sync<float> LowRT60;

    [Range(ReverbSettings.MinRT60, ReverbSettings.MaxRT60, "0.00", exponential: true)]
    public readonly Sync<float> MidRT60;

    // Where the decay time is half the mid one.
    [Range(ReverbSettings.MinHFDamping, ReverbSettings.MaxHFDamping, "0", exponential: true)]
    public readonly Sync<float> HFDamping;

    [Range(ReverbSettings.MinEQFrequency, ReverbSettings.MaxEQFrequency, "0", exponential: true)]
    public readonly Sync<float> EQ1Frequency;

    [Range(ReverbSettings.MinEQLevel, ReverbSettings.MaxEQLevel, "0.0")]
    public readonly Sync<float> EQ1Level;

    [Range(ReverbSettings.MinEQFrequency, ReverbSettings.MaxEQFrequency, "0", exponential: true)]
    public readonly Sync<float> EQ2Frequency;

    [Range(ReverbSettings.MinEQLevel, ReverbSettings.MaxEQLevel, "0.0")]
    public readonly Sync<float> EQ2Level;

    [Range(ReverbSettings.MinMix, ReverbSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    // On the reverb only; the dry signal is never touched by it.
    [Range(ReverbSettings.MinLevel, ReverbSettings.MaxLevel, "0.0")]
    public readonly Sync<float> Level;

    public ReverbEffect()
    {
        PreDelay = new Sync<float>(this, ReverbSettings.DefaultPreDelay);
        Crossover = new Sync<float>(this, ReverbSettings.DefaultCrossover);
        LowRT60 = new Sync<float>(this, ReverbSettings.DefaultLowRT60);
        MidRT60 = new Sync<float>(this, ReverbSettings.DefaultMidRT60);
        HFDamping = new Sync<float>(this, ReverbSettings.DefaultHFDamping);
        EQ1Frequency = new Sync<float>(this, ReverbSettings.DefaultEQ1Frequency);
        EQ1Level = new Sync<float>(this, ReverbSettings.DefaultEQ1Level);
        EQ2Frequency = new Sync<float>(this, ReverbSettings.DefaultEQ2Frequency);
        EQ2Level = new Sync<float>(this, ReverbSettings.DefaultEQ2Level);
        Mix = new Sync<float>(this, ReverbSettings.DefaultMix);
        Level = new Sync<float>(this, ReverbSettings.DefaultLevel);
    }

    // The fields as stored; the filter clamps what it is given to ReverbSettings' limits.
    public ReverbSettings Settings => Fields.Read();

    internal ReverbFields Fields
        => new(PreDelay, Crossover, LowRT60, MidRT60, HFDamping, EQ1Frequency, EQ1Level, EQ2Frequency, EQ2Level, Mix, Level);

    protected override IAudioDspFilter CreateFilter() => new ReverbFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((ReverbFilter)filter).Settings = Settings;
}
