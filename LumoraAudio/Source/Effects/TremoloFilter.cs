// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A tremolo: each side's level follows 1 - Depth * (1 - sin) / 2, so it swings between full and 1 - Depth. The sine
// is worked out exactly once per control block and followed in straight lines between; a new rate glides in
// octaves from the phase it is at, depth glides per sample and spread at control rate, so no change clicks.
public sealed class TremoloFilter : IAudioDspFilter
{
    private const int ControlBlock = 32;
    private const double GainSeconds = 0.015;
    private const double SpreadSeconds = 0.05;
    private const float GainSnap = 1e-6f;

    private readonly StagedSettings<TremoloSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private double _rateOctave;
    private double _rateOctaveTarget;
    private double _phase;
    private double _spread;
    private double _spreadTarget;
    private double _spreadCoefficient;
    private float _lfoLeft;
    private float _lfoRight;
    private float _stepLeft;
    private float _stepRight;
    private float _depth;
    private float _depthTarget;
    private float _depthCoefficient;
    private int _countdown;
    private bool _primed;

    public TremoloFilter()
        : this(AudioSimulator.DefaultSampleRate, TremoloSettings.Default)
    {
    }

    public TremoloFilter(int sampleRate, in TremoloSettings settings)
    {
        _settings = new StagedSettings<TremoloSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public TremoloSettings Settings
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
        if (_depth == 0f && _depthTarget == 0f)
            return;

        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_countdown <= 0)
            {
                PlanLfo();
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
        float lfoLeft = _lfoLeft;
        float lfoRight = _lfoRight;
        float stepLeft = _stepLeft;
        float stepRight = _stepRight;
        float depth = _depth;
        float depthTarget = _depthTarget;
        float k = _depthCoefficient;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (depth != depthTarget)
                depth = DspMath.Approach(depth, depthTarget, k, GainSnap);
            float half = 0.5f * depth;
            float left = 1f - half + half * lfoLeft;
            float right = 1f - half + half * lfoRight;
            lfoLeft += stepLeft;
            lfoRight += stepRight;
            buffer[i] = buffer[i].Scale(left, right);
        }
        _lfoLeft = lfoLeft;
        _lfoRight = lfoRight;
        _depth = depth;
    }

    private void PlanLfo()
    {
        DspMath.Glide(ref _spread, _spreadTarget, _spreadCoefficient, 1e-6);
        DspMath.Glide(ref _rateOctave, _rateOctaveTarget, _spreadCoefficient, 1e-6);
        double end = _phase + Math.Pow(2.0, _rateOctave) * ControlBlock / _sampleRate;
        end -= Math.Floor(end);
        _phase = end;
        _stepLeft = ((float)Math.Sin(2.0 * Math.PI * end) - _lfoLeft) * (1f / ControlBlock);
        _stepRight = ((float)Math.Sin(2.0 * Math.PI * (end + _spread)) - _lfoRight) * (1f / ControlBlock);
    }

    private void Retarget()
    {
        ref readonly TremoloSettings s = ref _settings.Current;
        _rateOctaveTarget = Math.Log2(s.Rate);
        _depthTarget = s.Depth;
        _spreadTarget = 0.5 * s.Spread;
    }

    private void Snap()
    {
        _depth = _depthTarget;
        _spread = _spreadTarget;
        _rateOctave = _rateOctaveTarget;
        _lfoLeft = (float)Math.Sin(2.0 * Math.PI * _phase);
        _lfoRight = (float)Math.Sin(2.0 * Math.PI * (_phase + _spread));
        _stepLeft = 0f;
        _stepRight = 0f;
        _countdown = 0;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _depthCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _spreadCoefficient = DspMath.OnePole(SpreadSeconds, sampleRate, ControlBlock);
        ClearState();
    }

    private void ClearState()
    {
        _phase = 0.0;
        Retarget();
        Snap();
    }
}
