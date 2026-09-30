// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A phaser: per side, a chain of one-pole all-pass stages sharing one corner, with the chain's output fed back
// to its input one sample late, crossfaded against the dry signal. Each stage turns the phase from 0 at DC to half
// a turn at Nyquist, a quarter turn at its corner, so N stages cancel the dry signal wherever their phase is an odd
// number of half turns, which is at tan(pi f / fs) = tan(pi fc / fs) * tan((2k + 1) pi / 2N): a notch for every two
// stages, spaced around the corner. The corner sweeps between two frequencies evenly in octaves.
//
// The stage coefficient is worked out exactly once per control block from the sweep and followed in a straight
// line between; a one-pole all-pass stays stable for every coefficient inside (-1, 1), and every point on that
// line is one. A change in the number of stages crossfades between the chain's output after the old count and
// after the new one; the extremes of the sweep and its rate glide in octaves, the spread at control rate,
// feedback and mix per sample. -xlinka
public sealed class PhaserFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.05;
    private const double GainSeconds = 0.015;
    private const double StageFadeSeconds = 0.03;
    private const double TailHoldSeconds = 0.1;
    private const double NyquistFraction = 0.45;
    private const float GainSnap = 1e-6f;
    private const float SilenceFloor = 1e-6f;
    private const float TailFloor = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private readonly StagedSettings<PhaserSettings> _settings;
    private readonly float[] _statesLeft = new float[PhaserSettings.MaxStages];
    private readonly float[] _statesRight = new float[PhaserSettings.MaxStages];
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private float _lastLeft;
    private float _lastRight;
    private float _coefficientLeft;
    private float _coefficientRight;
    private float _stepLeft;
    private float _stepRight;

    private double _rateOctave;
    private double _rateOctaveTarget;
    private double _phase;
    private double _minOctave;
    private double _minOctaveTarget;
    private double _maxOctave;
    private double _maxOctaveTarget;
    private double _spread;
    private double _spreadTarget;
    private double _parameterCoefficient;

    private int _stages;
    private int _stagesTarget;
    private int _fromStages;
    private float _stageFade = 1f;
    private float _stageFadeStep;

    private float _feedback;
    private float _feedbackTarget;
    private float _dry = 1f;
    private float _dryTarget = 1f;
    private float _wet;
    private float _wetTarget;
    private float _gainCoefficient;

    private int _countdown;
    private bool _primed;
    private bool _chainStale;
    private int _quietSamples = QuietForever;
    private float _wetPeak;

    public PhaserFilter()
        : this(AudioSimulator.DefaultSampleRate, PhaserSettings.Default)
    {
    }

    public PhaserFilter(int sampleRate, in PhaserSettings settings)
    {
        _settings = new StagedSettings<PhaserSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public PhaserSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public bool HasTail => (_wet > 0f || _wetTarget > 0f) && Ringing;

    private bool Ringing => _quietSamples < TailHoldSeconds * _sampleRate || _wetPeak > TailFloor;

    private bool Settled => _stageFade >= 1f && _stages == _stagesTarget && _feedback == _feedbackTarget && _dry == _dryTarget && _wet == _wetTarget;

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
        bool quiet = !(DspMath.Peak(buffer) > SilenceFloor);
        _quietSamples = quiet ? Math.Min(QuietForever, _quietSamples + buffer.Length) : 0;

        if (_wetTarget == 0f && _wet == 0f && Settled)
        {
            _chainStale = true;
            _wetPeak = 0f;
            ApplyDry(buffer);
            return;
        }
        if (_chainStale)
        {
            _chainStale = false;
            ClearChain();
        }
        if (quiet && Settled && !Ringing)
        {
            _wetPeak = 0f;
            ApplyDry(buffer);
            return;
        }

        float wetPeak = 0f;
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_countdown <= 0)
            {
                ControlUpdate();
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, buffer.Length - offset);
            float runPeak = Render(buffer.Slice(offset, run));
            if (runPeak > wetPeak)
                wetPeak = runPeak;
            _countdown -= run;
            offset += run;
        }
        _wetPeak = wetPeak;
    }

    private float Render(Span<StereoSample> buffer)
    {
        float[] statesLeft = _statesLeft;
        float[] statesRight = _statesRight;
        float lastLeft = _lastLeft;
        float lastRight = _lastRight;
        float coefficientLeft = _coefficientLeft;
        float coefficientRight = _coefficientRight;
        float stepLeft = _stepLeft;
        float stepRight = _stepRight;
        int stages = _stages;
        int fromStages = _fromStages;
        float feedback = _feedback;
        float feedbackTarget = _feedbackTarget;
        float dry = _dry;
        float dryTarget = _dryTarget;
        float wet = _wet;
        float wetTarget = _wetTarget;
        float k = _gainCoefficient;
        float peak = 0f;

        for (int i = 0; i < buffer.Length; i++)
        {
            coefficientLeft += stepLeft;
            coefficientRight += stepRight;
            if (feedback != feedbackTarget)
                feedback = DspMath.Approach(feedback, feedbackTarget, k, GainSnap);
            if (dry != dryTarget)
                dry = DspMath.Approach(dry, dryTarget, k, GainSnap);
            if (wet != wetTarget)
                wet = DspMath.Approach(wet, wetTarget, k, GainSnap);

            bool fading = _stageFade < 1f;
            int run = fading ? Math.Max(stages, fromStages) : stages;
            StereoSample x = buffer[i];
            float left = Chain(statesLeft, x.left + feedback * lastLeft, coefficientLeft, run, stages, fading ? fromStages : -1, out float oldLeft);
            float right = Chain(statesRight, x.right + feedback * lastRight, coefficientRight, run, stages, fading ? fromStages : -1, out float oldRight);
            if (fading)
            {
                float t = _stageFade;
                left = oldLeft + (left - oldLeft) * t;
                right = oldRight + (right - oldRight) * t;
                t += _stageFadeStep;
                if (t >= 1f)
                {
                    t = 1f;
                    // Stages past the new count go quiet and start clean if they are wanted again.
                    for (int s = stages; s < PhaserSettings.MaxStages; s++)
                    {
                        statesLeft[s] = 0f;
                        statesRight[s] = 0f;
                    }
                }
                _stageFade = t;
            }
            lastLeft = DspMath.Flush(left);
            lastRight = DspMath.Flush(right);

            float magnitude = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (magnitude > peak)
                peak = magnitude;
            buffer[i] = new StereoSample(x.left * dry + left * wet, x.right * dry + right * wet);
        }

        _lastLeft = lastLeft;
        _lastRight = lastRight;
        _coefficientLeft = coefficientLeft;
        _coefficientRight = coefficientRight;
        _feedback = feedback;
        _dry = dry;
        _wet = wet;
        return peak;
    }

    // Runs `run` stages; returns the output after `stages` of them and, when `from` is a count, the output after
    // that many in `old`.
    private static float Chain(float[] states, float input, float a, int run, int stages, int from, out float old)
    {
        float u = input;
        float atStages = input;
        old = input;
        for (int s = 0; s < run; s++)
        {
            float y = a * u + states[s];
            states[s] = DspMath.Flush(u - a * y);
            u = y;
            if (s + 1 == stages)
                atStages = u;
            if (s + 1 == from)
                old = u;
        }
        return atStages;
    }

    private void ControlUpdate()
    {
        if (_stageFade >= 1f && _stages != _stagesTarget)
        {
            _fromStages = _stages;
            _stages = _stagesTarget;
            _stageFade = 0f;
        }

        double k = _parameterCoefficient;
        DspMath.Glide(ref _minOctave, _minOctaveTarget, k, 1e-5);
        DspMath.Glide(ref _maxOctave, _maxOctaveTarget, k, 1e-5);
        DspMath.Glide(ref _spread, _spreadTarget, k, 1e-6);
        DspMath.Glide(ref _rateOctave, _rateOctaveTarget, k, 1e-6);

        double end = _phase + Math.Pow(2.0, _rateOctave) * ControlBlock / _sampleRate;
        end -= Math.Floor(end);
        _phase = end;
        float left = Coefficient(Math.Sin(2.0 * Math.PI * end));
        float right = Coefficient(Math.Sin(2.0 * Math.PI * (end + _spread)));
        _stepLeft = (left - _coefficientLeft) * (1f / ControlBlock);
        _stepRight = (right - _coefficientRight) * (1f / ControlBlock);
    }

    // All-pass coefficient for a point of the sweep, -1 at the bottom of it and 1 at the top.
    private float Coefficient(double sweep)
    {
        double octave = _minOctave + (_maxOctave - _minOctave) * (0.5 + 0.5 * sweep);
        double frequency = Math.Min(Math.Pow(2.0, octave), NyquistFraction * _sampleRate);
        double t = Math.Tan(Math.PI * frequency / _sampleRate);
        return (float)((t - 1.0) / (t + 1.0));
    }

    private void Retarget()
    {
        ref readonly PhaserSettings s = ref _settings.Current;
        _rateOctaveTarget = Math.Log2(s.Rate);
        _minOctaveTarget = Math.Log2(s.MinFrequency);
        _maxOctaveTarget = Math.Log2(s.MaxFrequency);
        _spreadTarget = 0.5 * s.Spread;
        _stagesTarget = s.Stages;
        _feedbackTarget = s.Feedback;
        DspMath.Crossfade(s.Mix, out _dryTarget, out _wetTarget);
    }

    private void Snap()
    {
        _minOctave = _minOctaveTarget;
        _maxOctave = _maxOctaveTarget;
        _spread = _spreadTarget;
        _rateOctave = _rateOctaveTarget;
        _stages = _stagesTarget;
        _fromStages = _stagesTarget;
        _stageFade = 1f;
        _feedback = _feedbackTarget;
        _dry = _dryTarget;
        _wet = _wetTarget;
        _coefficientLeft = Coefficient(Math.Sin(2.0 * Math.PI * _phase));
        _coefficientRight = Coefficient(Math.Sin(2.0 * Math.PI * (_phase + _spread)));
        _stepLeft = 0f;
        _stepRight = 0f;
        _countdown = 0;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _parameterCoefficient = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _stageFadeStep = (float)(1.0 / (StageFadeSeconds * sampleRate));
        ClearState();
    }

    private void ClearState()
    {
        ClearChain();
        _chainStale = false;
        _phase = 0.0;
        _quietSamples = QuietForever;
        _wetPeak = 0f;
        Retarget();
        Snap();
    }

    private void ClearChain()
    {
        Array.Clear(_statesLeft);
        Array.Clear(_statesRight);
        _lastLeft = 0f;
        _lastRight = 0f;
    }

    private void ApplyDry(Span<StereoSample> buffer)
    {
        float dry = _dry;
        if (dry == 1f)
            return;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = buffer[i].Multiply(dry);
    }
}
