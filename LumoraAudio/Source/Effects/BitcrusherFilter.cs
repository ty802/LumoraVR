// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A bitcrusher: a sample-and-hold at a lower rate, rounding each held sample to a grid of 2^(bits - 1) levels per
// side of zero. At a whole number of bits the spacing is a power of two, so every sample out is exactly on the
// grid. The aliasing is the point, so nothing is filtered. Bits glide in bits and the rate in octaves, both at
// control rate; a glide from 16 bits down to 4 makes the grid coarser a little at a time rather than all at once.
// Mix glides per sample.
public sealed class BitcrusherFilter : IAudioDspFilter
{
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.02;
    private const double GainSeconds = 0.015;
    private const float GainSnap = 1e-6f;

    private readonly StagedSettings<BitcrusherSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private double _bits;
    private double _bitsTarget;
    private double _rateOctave;
    private double _rateOctaveTarget;
    private double _parameterCoefficient;
    private bool _parametersSettled;
    private float _step;
    private float _inverseStep;
    private double _increment;
    private double _phase;
    private float _heldLeft;
    private float _heldRight;

    private float _dry;
    private float _dryTarget;
    private float _wet = 1f;
    private float _wetTarget = 1f;
    private float _gainCoefficient;
    private int _countdown;
    private bool _primed;

    public BitcrusherFilter()
        : this(AudioSimulator.DefaultSampleRate, BitcrusherSettings.Default)
    {
    }

    public BitcrusherFilter(int sampleRate, in BitcrusherSettings settings)
    {
        _settings = new StagedSettings<BitcrusherSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public BitcrusherSettings Settings
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
            ClearState();
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
                ControlUpdate();
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, buffer.Length - offset);
            Render(buffer.Slice(offset, run));
            _countdown -= run;
            offset += run;
        }
    }

    private void Render(Span<StereoSample> buffer)
    {
        float step = _step;
        float inverseStep = _inverseStep;
        double increment = _increment;
        double phase = _phase;
        float heldLeft = _heldLeft;
        float heldRight = _heldRight;
        float dry = _dry;
        float dryTarget = _dryTarget;
        float wet = _wet;
        float wetTarget = _wetTarget;
        float k = _gainCoefficient;

        for (int i = 0; i < buffer.Length; i++)
        {
            StereoSample x = buffer[i];
            // The allowance lets a rate that divides the mixer's evenly hold for exactly that many samples every
            // time, instead of one sample more whenever the sum of the increments rounds under 1.
            phase += increment;
            if (phase >= 1.0 - 1e-9)
            {
                phase -= 1.0;
                heldLeft = MathF.Round(x.left * inverseStep) * step;
                heldRight = MathF.Round(x.right * inverseStep) * step;
            }
            if (dry != dryTarget)
                dry = DspMath.Approach(dry, dryTarget, k, GainSnap);
            if (wet != wetTarget)
                wet = DspMath.Approach(wet, wetTarget, k, GainSnap);
            buffer[i] = dry == 0f && wet == 1f
                ? new StereoSample(heldLeft, heldRight)
                : new StereoSample(x.left * dry + heldLeft * wet, x.right * dry + heldRight * wet);
        }

        _phase = phase;
        _heldLeft = heldLeft;
        _heldRight = heldRight;
        _dry = dry;
        _wet = wet;
    }

    private void ControlUpdate()
    {
        if (_parametersSettled)
            return;
        bool moving = DspMath.Glide(ref _bits, _bitsTarget, _parameterCoefficient, 1e-5);
        moving |= DspMath.Glide(ref _rateOctave, _rateOctaveTarget, _parameterCoefficient, 1e-5);
        Publish();
        _parametersSettled = !moving;
    }

    private void Publish()
    {
        _step = (float)Math.Pow(2.0, 1.0 - _bits);
        _inverseStep = 1f / _step;
        _increment = Math.Min(1.0, Math.Pow(2.0, _rateOctave) / _sampleRate);
    }

    private void Retarget()
    {
        ref readonly BitcrusherSettings s = ref _settings.Current;
        _bitsTarget = s.Bits;
        _rateOctaveTarget = Math.Log2(s.Rate);
        DspMath.Crossfade(s.Mix, out _dryTarget, out _wetTarget);
        _parametersSettled = false;
    }

    private void Snap()
    {
        _bits = _bitsTarget;
        _rateOctave = _rateOctaveTarget;
        Publish();
        _parametersSettled = true;
        _dry = _dryTarget;
        _wet = _wetTarget;
        _countdown = 0;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _parameterCoefficient = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        ClearState();
    }

    // A cleared crusher takes the next sample in straight away.
    private void ClearState()
    {
        _phase = 1.0;
        _heldLeft = 0f;
        _heldRight = 0f;
        Retarget();
        Snap();
    }
}
