// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// An echo: one delay line per side, read a whole number of samples back, with a low-pass and a high-pass in the
// feedback path so each repeat comes back darker and thinner than the one before. The filters sit only in the
// loop, so repeat 1 is the input itself, exactly Time late; repeat n has been round the loop n - 1 times.
// Both filters are trapezoidal one-poles that never pass more than they are given, so with the feedback held
// under 1 the loop dies away at every setting, moving or not.
//
// Ping-pong feeds a mono sum of the input into the left line and crosses the feedback over, so repeat 1 is on the
// left, repeat 2 on the right, and so on, each at the same level a stereo echo would have.
//
// A new time crossfades from the old read head to the new one (no pitch bend, no click), the corners glide in
// octaves and the feedback and mix glide per sample. A delay whose echoes have died down and whose input is
// silent stops computing. -xlinka
public sealed class DelayFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int ControlBlock = 32;
    private const double ParameterSeconds = 0.03;
    private const double GainSeconds = 0.015;
    private const double TapFadeSeconds = 0.03;
    private const float GainSnap = 1e-6f;
    private const float SilenceFloor = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private readonly StagedSettings<DelaySettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();
    private int _write;
    private TapFade _tap;

    private float _lowLeft;
    private float _lowRight;
    private float _cutLeft;
    private float _cutRight;
    private double _highCutOctave;
    private double _highCutTarget;
    private double _lowCutOctave;
    private double _lowCutTarget;
    private float _highCutGain;
    private float _lowCutGain;
    private double _parameterCoefficient;
    private bool _cornersSettled;

    private float _feedback;
    private float _feedbackTarget;
    private float _dry = 1f;
    private float _dryTarget = 1f;
    private float _wet;
    private float _wetTarget;
    private float _gainCoefficient;
    private bool _pingPong;

    private int _countdown;
    private bool _primed;
    private bool _linesStale;
    private int _writtenQuiet = QuietForever;

    public DelayFilter()
        : this(AudioSimulator.DefaultSampleRate, DelaySettings.Default)
    {
    }

    public DelayFilter(int sampleRate, in DelaySettings settings)
    {
        _settings = new StagedSettings<DelaySettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public DelaySettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    // Audio thread only: echoes of input that has stopped are still to come.
    public bool HasTail => (_wet > 0f || _wetTarget > 0f) && Ringing;

    public void Reset() => _resetRequested = true;

    private bool Settled => _cornersSettled && _tap.Settled && _dry == _dryTarget && _wet == _wetTarget && _feedback == _feedbackTarget;

    // Something at or above the floor went into a line recently enough that it has not all come out.
    private bool Ringing => _writtenQuiet < _tap.Longest + ControlBlock;

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

        if (_wetTarget == 0f && _wet == 0f && Settled)
        {
            // The echoes are off; what the lines hold is stale by the time they are back.
            _linesStale = true;
            ApplyDry(buffer);
            return;
        }
        if (_linesStale)
        {
            _linesStale = false;
            ClearLines();
        }
        if (quiet && Settled && !Ringing)
        {
            ApplyDry(buffer);
            return;
        }

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
        float lowLeft = _lowLeft;
        float lowRight = _lowRight;
        float cutLeft = _cutLeft;
        float cutRight = _cutRight;
        float highCutGain = _highCutGain;
        float lowCutGain = _lowCutGain;
        float feedback = _feedback;
        float feedbackTarget = _feedbackTarget;
        float dry = _dry;
        float dryTarget = _dryTarget;
        float wet = _wet;
        float wetTarget = _wetTarget;
        float k = _gainCoefficient;
        bool pingPong = _pingPong;
        int writtenQuiet = _writtenQuiet;

        for (int i = 0; i < buffer.Length; i++)
        {
            StereoSample x = buffer[i];
            int at = TapFade.Index(write, _tap.Tap, size);
            float readLeft = left[at];
            float readRight = right[at];
            if (_tap.Fading)
            {
                int other = TapFade.Index(write, _tap.Next, size);
                float t = _tap.Fade;
                readLeft += (left[other] - readLeft) * t;
                readRight += (right[other] - readRight) * t;
                _tap.Advance();
            }

            float loopLeft = LoopFilter(readLeft, ref lowLeft, ref cutLeft, highCutGain, lowCutGain);
            float loopRight = LoopFilter(readRight, ref lowRight, ref cutRight, highCutGain, lowCutGain);
            if (feedback != feedbackTarget)
                feedback = DspMath.Approach(feedback, feedbackTarget, k, GainSnap);

            float intoLeft;
            float intoRight;
            if (pingPong)
            {
                intoLeft = DspMath.Flush(0.5f * (x.left + x.right) + feedback * loopRight);
                intoRight = DspMath.Flush(feedback * loopLeft);
            }
            else
            {
                intoLeft = DspMath.Flush(x.left + feedback * loopLeft);
                intoRight = DspMath.Flush(x.right + feedback * loopRight);
            }
            left[write] = intoLeft;
            right[write] = intoRight;
            if (++write == size)
                write = 0;
            writtenQuiet = MathF.Max(MathF.Abs(intoLeft), MathF.Abs(intoRight)) > SilenceFloor ? 0 : Math.Min(QuietForever, writtenQuiet + 1);

            if (dry != dryTarget)
                dry = DspMath.Approach(dry, dryTarget, k, GainSnap);
            if (wet != wetTarget)
                wet = DspMath.Approach(wet, wetTarget, k, GainSnap);
            buffer[i] = new StereoSample(x.left * dry + readLeft * wet, x.right * dry + readRight * wet);
        }

        _write = write;
        _lowLeft = lowLeft;
        _lowRight = lowRight;
        _cutLeft = cutLeft;
        _cutRight = cutRight;
        _feedback = feedback;
        _dry = dry;
        _wet = wet;
        _writtenQuiet = writtenQuiet;
    }

    // Low-pass at the high cut, then the low-pass at the low cut taken away from what is left: both trapezoidal
    // one-poles on states that are flushed before they can go subnormal.
    private static float LoopFilter(float x, ref float low, ref float cut, float highCutGain, float lowCutGain)
    {
        float v = (x - low) * highCutGain;
        float lowPassed = v + low;
        low = DspMath.Flush(lowPassed + v);
        float w = (lowPassed - cut) * lowCutGain;
        float bass = w + cut;
        cut = DspMath.Flush(bass + w);
        return lowPassed - bass;
    }

    private void ControlUpdate()
    {
        _tap.Begin();
        if (_cornersSettled)
            return;
        bool moving = DspMath.Glide(ref _highCutOctave, _highCutTarget, _parameterCoefficient, 1e-5);
        moving |= DspMath.Glide(ref _lowCutOctave, _lowCutTarget, _parameterCoefficient, 1e-5);
        Corners();
        _cornersSettled = !moving;
    }

    private void Corners()
    {
        double rate = _sampleRate;
        double high = Math.Tan(Math.PI * Math.Min(Math.Pow(2.0, _highCutOctave), 0.49 * rate) / rate);
        double low = Math.Tan(Math.PI * Math.Min(Math.Pow(2.0, _lowCutOctave), 0.49 * rate) / rate);
        _highCutGain = (float)(high / (1.0 + high));
        _lowCutGain = (float)(low / (1.0 + low));
    }

    private void Retarget()
    {
        ref readonly DelaySettings s = ref _settings.Current;
        _tap.Target = Math.Clamp((int)Math.Round(s.Time * 0.001 * _sampleRate), 1, _left.Length - 1);
        _highCutTarget = Math.Log2(s.HighCut);
        _lowCutTarget = Math.Log2(s.LowCut);
        _feedbackTarget = s.Feedback;
        _pingPong = s.PingPong;
        DspMath.WetAndDry(s.Mix, out _dryTarget, out _wetTarget);
        _cornersSettled = false;
    }

    private void Snap()
    {
        _tap.Snap();
        _highCutOctave = _highCutTarget;
        _lowCutOctave = _lowCutTarget;
        Corners();
        _cornersSettled = true;
        _feedback = _feedbackTarget;
        _dry = _dryTarget;
        _wet = _wetTarget;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        int size = (int)Math.Ceiling(DelaySettings.MaxTime * 0.001 * sampleRate) + 2;
        _left = new float[size];
        _right = new float[size];
        _parameterCoefficient = DspMath.OnePole(ParameterSeconds, sampleRate, ControlBlock);
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _tap.Increment = (float)(1.0 / (TapFadeSeconds * sampleRate));
        ClearState();
    }

    private void ClearState()
    {
        ClearLines();
        _linesStale = false;
        _countdown = 0;
        Retarget();
        Snap();
    }

    private void ClearLines()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _write = 0;
        _lowLeft = 0f;
        _lowRight = 0f;
        _cutLeft = 0f;
        _cutRight = 0f;
        _writtenQuiet = QuietForever;
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
