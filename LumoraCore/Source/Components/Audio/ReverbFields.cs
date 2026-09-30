// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// The eleven fields a reverb keeps, taken as one: a ReverbEffect and a ReverbZone hold the same set, and
// whatever reads or writes a whole reverb value (the filter, a script) goes through here, in
// ReverbSettings order.
internal readonly struct ReverbFields
{
    public const int Count = 11;

    private readonly Sync<float> _preDelay;
    private readonly Sync<float> _crossover;
    private readonly Sync<float> _lowRT60;
    private readonly Sync<float> _midRT60;
    private readonly Sync<float> _hfDamping;
    private readonly Sync<float> _eq1Frequency;
    private readonly Sync<float> _eq1Level;
    private readonly Sync<float> _eq2Frequency;
    private readonly Sync<float> _eq2Level;
    private readonly Sync<float> _mix;
    private readonly Sync<float> _level;

    public ReverbFields(Sync<float> preDelay, Sync<float> crossover, Sync<float> lowRT60, Sync<float> midRT60, Sync<float> hfDamping,
        Sync<float> eq1Frequency, Sync<float> eq1Level, Sync<float> eq2Frequency, Sync<float> eq2Level, Sync<float> mix, Sync<float> level)
    {
        _preDelay = preDelay;
        _crossover = crossover;
        _lowRT60 = lowRT60;
        _midRT60 = midRT60;
        _hfDamping = hfDamping;
        _eq1Frequency = eq1Frequency;
        _eq1Level = eq1Level;
        _eq2Frequency = eq2Frequency;
        _eq2Level = eq2Level;
        _mix = mix;
        _level = level;
    }

    public Sync<float> this[int index] => index switch
    {
        0 => _preDelay,
        1 => _crossover,
        2 => _lowRT60,
        3 => _midRT60,
        4 => _hfDamping,
        5 => _eq1Frequency,
        6 => _eq1Level,
        7 => _eq2Frequency,
        8 => _eq2Level,
        9 => _mix,
        _ => _level,
    };

    // As stored, not clamped: the filter clamps, and a value read back is the value written.
    public ReverbSettings Read() => new()
    {
        PreDelay = _preDelay.Value,
        Crossover = _crossover.Value,
        LowRT60 = _lowRT60.Value,
        MidRT60 = _midRT60.Value,
        HFDamping = _hfDamping.Value,
        EQ1Frequency = _eq1Frequency.Value,
        EQ1Level = _eq1Level.Value,
        EQ2Frequency = _eq2Frequency.Value,
        EQ2Level = _eq2Level.Value,
        Mix = _mix.Value,
        Level = _level.Value,
    };

    public void Write(in ReverbSettings settings)
    {
        _preDelay.Value = settings.PreDelay;
        _crossover.Value = settings.Crossover;
        _lowRT60.Value = settings.LowRT60;
        _midRT60.Value = settings.MidRT60;
        _hfDamping.Value = settings.HFDamping;
        _eq1Frequency.Value = settings.EQ1Frequency;
        _eq1Level.Value = settings.EQ1Level;
        _eq2Frequency.Value = settings.EQ2Frequency;
        _eq2Level.Value = settings.EQ2Level;
        _mix.Value = settings.Mix;
        _level.Value = settings.Level;
    }
}
