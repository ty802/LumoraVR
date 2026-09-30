// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A feed-forward compressor, stereo-linked so the image never leans.
//
// The detector reads the input as it arrives: the louder channel's peak, or the power of both averaged over
// 10 ms. That level goes through the static curve in decibels (flat below the knee, a quadratic through it,
// 1/ratio above it), and the gain reduction that comes out is smoothed in decibels by two one-poles picked per
// sample: the attack one while the reduction is deepening, the release one while it lets go. Attack and release
// are therefore true time constants of the gain in decibels, whatever the ratio. The audio itself can run up to
// 10 ms behind the detector, so the gain is already down when a transient reaches the output; that delay is the
// only latency, and LatencySamples reports it.
//
// Threshold, knee and ratio glide at control rate, makeup per sample, a switch of detector crossfades the two
// levels, and a new lookahead crossfades between read heads, so a jump in any setting reaches the output as a
// glide. -xlinka
public sealed class CompressorFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.02;
    private const double MakeupSeconds = 0.02;
    private const double RmsSeconds = 0.01;
    private const double TapFadeSeconds = 0.02;
    private const float ReductionSnap = 1e-6f;
    private const float MakeupSnap = 1e-5f;
    private const float SilenceFloor = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private readonly StagedSettings<CompressorSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();
    private int _write;
    private TapFade _tap;

    private double _threshold;
    private double _thresholdTarget;
    private double _knee;
    private double _kneeTarget;
    private double _slope;
    private double _slopeTarget;
    private double _blend;
    private double _blendTarget;
    private double _parameterCoefficient;
    private bool _parametersSettled;
    private float _thresholdNow;
    private float _kneeNow;
    private float _slopeNow;
    private float _blendNow;
    private float _kneeStart;

    private float _makeup;
    private float _makeupTarget;
    private float _makeupGain = 1f;
    private float _makeupCoefficient;

    private float _meanSquare;
    private float _rmsCoefficient;
    private float _reduction;
    private float _attack;
    private float _release;

    private int _countdown;
    private bool _primed;
    private int _quietSamples = QuietForever;

    public CompressorFilter()
        : this(AudioSimulator.DefaultSampleRate, CompressorSettings.Default)
    {
    }

    public CompressorFilter(int sampleRate, in CompressorSettings settings)
    {
        _settings = new StagedSettings<CompressorSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public CompressorSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    // How far the output runs behind the input, from the lookahead asked for.
    public int LatencySamples => (int)Math.Round(_settings.Value.Lookahead * 0.001 * _sampleRate);

    // Audio thread only: the lookahead line still holds sound that has not come out.
    public bool HasTail => _tap.Longest > 0 && _quietSamples < _tap.Longest + ControlBlock;

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
        float peak = DspMath.Peak(buffer);
        _quietSamples = peak > SilenceFloor ? 0 : Math.Min(QuietForever, _quietSamples + buffer.Length);

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
        float[] left = _left;
        float[] right = _right;
        int size = left.Length;
        int write = _write;
        float threshold = _thresholdNow;
        float knee = _kneeNow;
        float slope = _slopeNow;
        float blend = _blendNow;
        float kneeStart = _kneeStart;
        float meanSquare = _meanSquare;
        float rmsCoefficient = _rmsCoefficient;
        float reduction = _reduction;
        float attack = _attack;
        float release = _release;
        float makeup = _makeup;
        float makeupTarget = _makeupTarget;
        float makeupGain = _makeupGain;
        float makeupCoefficient = _makeupCoefficient;

        for (int i = 0; i < buffer.Length; i++)
        {
            StereoSample x = buffer[i];
            float l = x.left;
            float r = x.right;

            left[write] = l;
            right[write] = r;
            int at = TapFade.Index(write, _tap.Tap, size);
            float delayedLeft = left[at];
            float delayedRight = right[at];
            if (_tap.Fading)
            {
                int other = TapFade.Index(write, _tap.Next, size);
                float t = _tap.Fade;
                delayedLeft += (left[other] - delayedLeft) * t;
                delayedRight += (right[other] - delayedRight) * t;
                _tap.Advance();
            }
            if (++write == size)
                write = 0;

            float level = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            meanSquare = DspMath.Flush(meanSquare + (0.5f * (l * l + r * r) - meanSquare) * rmsCoefficient);
            if (blend > 0f)
                level += (MathF.Sqrt(meanSquare) - level) * blend;

            float target = 0f;
            if (level > kneeStart)
            {
                float over = MathF.Log(level) * DspMath.TwentyOverLn10 - threshold;
                if (knee > 0f && 2f * over < knee)
                {
                    float into = over + 0.5f * knee;
                    target = slope * into * into / (2f * knee);
                }
                else if (over > 0f)
                {
                    target = slope * over;
                }
            }
            if (reduction != target)
                reduction = DspMath.Approach(reduction, target, target < reduction ? attack : release, ReductionSnap);

            float gain;
            if (makeup != makeupTarget)
            {
                makeup = DspMath.Approach(makeup, makeupTarget, makeupCoefficient, MakeupSnap);
                gain = DspMath.DbToGain(reduction + makeup);
            }
            else
            {
                gain = reduction == 0f ? makeupGain : DspMath.DbToGain(reduction + makeup);
            }
            buffer[i] = new StereoSample(delayedLeft * gain, delayedRight * gain);
        }

        _write = write;
        _meanSquare = meanSquare;
        _reduction = reduction;
        _makeup = makeup;
    }

    private void ControlUpdate()
    {
        _tap.Begin();
        if (_parametersSettled)
            return;
        double k = _parameterCoefficient;
        bool moving = DspMath.Glide(ref _threshold, _thresholdTarget, k, 1e-4);
        moving |= DspMath.Glide(ref _knee, _kneeTarget, k, 1e-4);
        moving |= DspMath.Glide(ref _slope, _slopeTarget, k, 1e-6);
        moving |= DspMath.Glide(ref _blend, _blendTarget, k, 1e-4);
        Publish();
        _parametersSettled = !moving;
    }

    private void Publish()
    {
        _thresholdNow = (float)_threshold;
        _kneeNow = (float)_knee;
        _slopeNow = (float)_slope;
        _blendNow = (float)_blend;
        // Below the bottom of the knee the curve is flat, so the logarithm is only taken above it.
        _kneeStart = (float)DspMath.DbToGain(_threshold - 0.5 * _knee);
    }

    private void Retarget()
    {
        ref readonly CompressorSettings s = ref _settings.Current;
        double rate = _sampleRate;
        _thresholdTarget = s.Threshold;
        _kneeTarget = s.Knee;
        _slopeTarget = 1.0 / s.Ratio - 1.0;
        _blendTarget = s.Detector == CompressorDetector.Rms ? 1.0 : 0.0;
        _attack = DspMath.OnePole(s.Attack * 0.001, rate);
        _release = DspMath.OnePole(s.Release * 0.001, rate);
        _makeupTarget = s.Makeup;
        _makeupGain = DspMath.DbToGain(s.Makeup);
        _tap.Target = Math.Clamp((int)Math.Round(s.Lookahead * 0.001 * rate), 0, _left.Length - 1);
        _parametersSettled = false;
    }

    private void Snap()
    {
        _threshold = _thresholdTarget;
        _knee = _kneeTarget;
        _slope = _slopeTarget;
        _blend = _blendTarget;
        _makeup = _makeupTarget;
        _tap.Snap();
        Publish();
        _parametersSettled = true;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        int size = (int)Math.Ceiling(CompressorSettings.MaxLookahead * 0.001 * sampleRate) + 2;
        _left = new float[size];
        _right = new float[size];
        _rmsCoefficient = DspMath.OnePole(RmsSeconds, sampleRate);
        _makeupCoefficient = DspMath.OnePole(MakeupSeconds, sampleRate);
        _parameterCoefficient = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _tap.Increment = (float)(1.0 / (TapFadeSeconds * sampleRate));
        ClearState();
    }

    private void ClearState()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _write = 0;
        _meanSquare = 0f;
        _reduction = 0f;
        _quietSamples = QuietForever;
        _countdown = 0;
        Retarget();
        Snap();
    }
}
