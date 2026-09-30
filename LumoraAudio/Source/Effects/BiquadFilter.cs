// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A single RBJ cookbook filter of any shape, with a low-pass or high-pass steepened by a Butterworth cascade.
// The sections are trapezoidal state-variable filters (SvfDesign), which give the cookbook curves exactly and
// keep still while being swept. The corner glides in octaves, Q and gain glide, and a change of shape or slope
// crossfades rather than switching, so nothing about it can click.
//
// Settings reach the audio thread as an immutable box swapped by reference and Process allocates nothing unless
// the sample rate it is handed changes. -xlinka
public sealed class BiquadFilter : IAudioDspFilter
{
    private const int MaxStages = 4;
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.03;
    private const double MorphSeconds = 0.02;

    private readonly StagedSettings<BiquadSettings> _settings;
    private readonly SvfChain _chain = new(MaxStages);
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;

    private int _sampleRate;
    private double _glide;
    private float _morphStep;
    private int _countdown;
    private bool _primed;

    public BiquadFilter()
        : this(AudioSimulator.DefaultSampleRate, BiquadSettings.Default)
    {
    }

    public BiquadFilter(int sampleRate, in BiquadSettings settings)
    {
        _settings = new StagedSettings<BiquadSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    // Written from any thread, picked up at the top of the next Process.
    public BiquadSettings Settings
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
            _chain.Snap(_sampleRate);
            _countdown = 0;
        }
        if (_settings.Take())
        {
            Retarget();
            if (!_primed)
                _chain.Snap(_sampleRate);
        }
        _primed = true;

        SampleBuffers.Sanitize(buffer);
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_countdown <= 0)
            {
                _chain.Control(_sampleRate, _glide, _morphStep, ControlBlock);
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, buffer.Length - offset);
            _chain.Run(buffer.Slice(offset, run));
            _countdown -= run;
            offset += run;
        }
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _glide = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _morphStep = (float)(ControlBlock / (MorphSeconds * sampleRate));
        Retarget();
        _chain.Snap(sampleRate);
        _countdown = 0;
    }

    private void Retarget()
    {
        ref readonly BiquadSettings s = ref _settings.Current;
        _chain.SetTarget(s.Type, s.Stages, s.Frequency, s.Q, s.Gain);
    }
}
