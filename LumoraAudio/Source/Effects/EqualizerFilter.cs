// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A parametric equalizer of up to eight bands in series, each one cookbook section of its own type, then an
// output gain. Every band glides and crossfades exactly as a BiquadFilter does, so a band's corner can be dragged
// across the spectrum, its type changed or the band switched off while music plays, and a band switched off or
// past the end of the list costs nothing. Because the bands are in series, their responses multiply: in
// decibels the curve is the sum of the bands' curves. -xlinka
public sealed class EqualizerFilter : IAudioDspFilter
{
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.03;
    private const double MorphSeconds = 0.02;
    private const double GainSeconds = 0.015;
    private const float GainSnap = 1e-6f;

    private readonly StagedSettings<EqualizerSettings> _settings;
    private readonly SvfChain[] _bands = new SvfChain[EqualizerSettings.MaxBands];
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;

    private int _sampleRate;
    private double _glide;
    private float _morphStep;
    private float _gainCoefficient;
    private float _gain = 1f;
    private float _gainTarget = 1f;
    private int _countdown;
    private bool _primed;

    public EqualizerFilter()
        : this(AudioSimulator.DefaultSampleRate, EqualizerSettings.Default)
    {
    }

    public EqualizerFilter(int sampleRate, in EqualizerSettings settings)
    {
        for (int i = 0; i < _bands.Length; i++)
            _bands[i] = new SvfChain(1);
        _settings = new StagedSettings<EqualizerSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public EqualizerSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public void Reset() => _resetRequested = true;

    public void Process(Span<StereoSample> buffer, int sampleRate)
    {
        if (buffer.IsEmpty)
            return;
        if (sampleRate > 0 && sampleRate != _sampleRate)
            Configure(sampleRate);
        if (_resetRequested)
        {
            _resetRequested = false;
            _settings.Take();
            Retarget();
            Snap();
        }
        if (_settings.Take())
        {
            Retarget();
            if (!_primed)
                Snap();
        }
        _primed = true;

        SampleBuffers.Sanitize(buffer);
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_countdown <= 0)
            {
                for (int b = 0; b < _bands.Length; b++)
                    _bands[b].Control(_sampleRate, _glide, _morphStep, ControlBlock);
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, buffer.Length - offset);
            Span<StereoSample> slice = buffer.Slice(offset, run);
            for (int b = 0; b < _bands.Length; b++)
            {
                SvfChain band = _bands[b];
                if (!band.IsIdle)
                    band.Run(slice);
            }
            _countdown -= run;
            offset += run;
        }
        ApplyGain(buffer);
    }

    private void ApplyGain(Span<StereoSample> buffer)
    {
        float gain = _gain;
        float target = _gainTarget;
        if (gain == target)
        {
            if (gain != 1f)
            {
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = buffer[i].Multiply(gain);
            }
            return;
        }
        float k = _gainCoefficient;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (gain != target)
                gain = DspMath.Approach(gain, target, k, GainSnap);
            buffer[i] = buffer[i].Multiply(gain);
        }
        _gain = gain;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _glide = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _morphStep = (float)(ControlBlock / (MorphSeconds * sampleRate));
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        Retarget();
        Snap();
    }

    private void Snap()
    {
        for (int b = 0; b < _bands.Length; b++)
            _bands[b].Snap(_sampleRate);
        _gain = _gainTarget;
        _countdown = 0;
    }

    private void Retarget()
    {
        ref readonly EqualizerSettings s = ref _settings.Current;
        for (int b = 0; b < _bands.Length; b++)
        {
            if (b < s.BandCount)
            {
                EqualizerBandSettings band = s.Bands[b];
                _bands[b].SetTarget(band.Type, band.Enabled ? 1 : 0, band.Frequency, band.Q, band.Gain);
            }
            else
            {
                _bands[b].SetCount(0);
            }
        }
        _gainTarget = DspMath.DbToGain(s.OutputGain);
    }
}
