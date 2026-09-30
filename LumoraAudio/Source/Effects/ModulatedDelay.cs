// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Audio;

// The engine under the chorus and the flanger: per side, a delay line read at a delay swept by a sine, with some
// of what is read fed back in, crossfaded against the dry signal.
//
// The read is 4-point cubic Hermite (Catmull-Rom). Linear interpolation dulls the top octave and makes the dulling
// come and go with the sweep, which is audible as a flutter on cymbals; the Hermite kernel is flat much further up
// and, unlike the 4-point Lagrange one, continuous in its slope, so the read head crossing a sample boundary makes
// no small click of its own. The sine is worked out exactly once per control block and followed in straight lines
// between, which at a 32-sample block stays within 0.03 percent of the curve even at the fastest rate. The right
// side runs the same sine a fraction of a cycle along (Spread), so a mono source comes out wide.
//
// The centre and the width of the sweep glide per sample, which bends the pitch a little as a change lands
// rather than clicking; feedback and mix glide per sample; the spread, and the rate in octaves, at control rate,
// so not even the slope of the sweep jumps. -xlinka
internal sealed class ModulatedDelay
{
    private const int ControlBlock = 32;
    private const double DelaySeconds = 0.05;
    private const double GainSeconds = 0.015;
    private const double SpreadSeconds = 0.05;
    private const float MinDelaySamples = 2f;
    private const float GainSnap = 1e-6f;
    private const float DelaySnap = 1e-4f;
    private const float SilenceFloor = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();
    private int _mask;
    private int _write;
    private int _sampleRate;
    private float _maxDelay;

    private double _rateOctave;
    private double _rateOctaveTarget;
    private double _phase;
    private float _lfoLeft;
    private float _lfoRight;
    private float _lfoLeftStep;
    private float _lfoRightStep;
    private double _spread;
    private double _spreadTarget;
    private double _spreadCoefficient;

    private float _center;
    private float _centerTarget;
    private float _amplitude;
    private float _amplitudeTarget;
    private float _delayCoefficient;
    private float _feedback;
    private float _feedbackTarget;
    private float _dry = 1f;
    private float _dryTarget = 1f;
    private float _wet;
    private float _wetTarget;
    private float _gainCoefficient;

    private int _countdown;
    private int _writtenQuiet = QuietForever;
    private bool _linesStale;

    public bool HasTail => (_wet > 0f || _wetTarget > 0f) && Ringing;

    // Something at or above the floor went into a line recently enough that it has not all come out.
    private bool Ringing => _writtenQuiet < (int)_maxDelay + ControlBlock;

    public void Configure(int sampleRate, float maxDelayMs)
    {
        _sampleRate = sampleRate;
        _maxDelay = (float)(maxDelayMs * 0.001 * sampleRate) + 1f;
        int size = DspMath.NextPowerOfTwo((int)_maxDelay + 8);
        _left = new float[size];
        _right = new float[size];
        _mask = size - 1;
        _delayCoefficient = DspMath.OnePole(DelaySeconds, sampleRate);
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _spreadCoefficient = DspMath.OnePole(SpreadSeconds, sampleRate, ControlBlock);
        Clear();
    }

    // Centre and swing in milliseconds (the delay runs from centre - swing to centre + swing), spread as a
    // fraction of a cycle, mix as a crossfade.
    public void SetTargets(double rate, double centerMs, double swingMs, float feedback, double spread, float mix)
    {
        double toSamples = 0.001 * _sampleRate;
        _rateOctaveTarget = Math.Log2(rate);
        _centerTarget = (float)(centerMs * toSamples);
        _amplitudeTarget = (float)(swingMs * toSamples);
        _feedbackTarget = feedback;
        _spreadTarget = spread;
        DspMath.Crossfade(mix, out _dryTarget, out _wetTarget);
    }

    public void Snap()
    {
        _center = _centerTarget;
        _amplitude = _amplitudeTarget;
        _feedback = _feedbackTarget;
        _dry = _dryTarget;
        _wet = _wetTarget;
        _spread = _spreadTarget;
        _rateOctave = _rateOctaveTarget;
        _lfoLeft = (float)Math.Sin(2.0 * Math.PI * _phase);
        _lfoRight = (float)Math.Sin(2.0 * Math.PI * (_phase + _spread));
        _lfoLeftStep = 0f;
        _lfoRightStep = 0f;
        _countdown = 0;
    }

    public void Clear()
    {
        ClearLines();
        _linesStale = false;
        _phase = 0.0;
        Snap();
    }

    private void ClearLines()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _write = 0;
        _writtenQuiet = QuietForever;
    }

    public void Process(Span<StereoSample> buffer)
    {
        if (_wetTarget == 0f && _wet == 0f && Settled)
        {
            // Nothing of the effect is heard; what the lines hold is stale by the time it is.
            _linesStale = true;
            ApplyDry(buffer);
            return;
        }
        if (_linesStale)
        {
            _linesStale = false;
            ClearLines();
        }
        if (!(DspMath.Peak(buffer) > SilenceFloor) && Settled && !Ringing)
        {
            ApplyDry(buffer);
            return;
        }

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

    private bool Settled => _center == _centerTarget && _amplitude == _amplitudeTarget && _feedback == _feedbackTarget
                            && _dry == _dryTarget && _wet == _wetTarget;

    private void PlanLfo()
    {
        DspMath.Glide(ref _spread, _spreadTarget, _spreadCoefficient, 1e-6);
        DspMath.Glide(ref _rateOctave, _rateOctaveTarget, _spreadCoefficient, 1e-6);
        double end = _phase + Math.Pow(2.0, _rateOctave) * ControlBlock / _sampleRate;
        end -= Math.Floor(end);
        float leftEnd = (float)Math.Sin(2.0 * Math.PI * end);
        float rightEnd = (float)Math.Sin(2.0 * Math.PI * (end + _spread));
        _lfoLeftStep = (leftEnd - _lfoLeft) * (1f / ControlBlock);
        _lfoRightStep = (rightEnd - _lfoRight) * (1f / ControlBlock);
        _phase = end;
    }

    private void Render(Span<StereoSample> buffer)
    {
        float[] left = _left;
        float[] right = _right;
        int mask = _mask;
        int write = _write;
        float maxDelay = _maxDelay;
        float lfoLeft = _lfoLeft;
        float lfoRight = _lfoRight;
        float lfoLeftStep = _lfoLeftStep;
        float lfoRightStep = _lfoRightStep;
        float center = _center;
        float centerTarget = _centerTarget;
        float amplitude = _amplitude;
        float amplitudeTarget = _amplitudeTarget;
        float delayCoefficient = _delayCoefficient;
        float feedback = _feedback;
        float feedbackTarget = _feedbackTarget;
        float dry = _dry;
        float dryTarget = _dryTarget;
        float wet = _wet;
        float wetTarget = _wetTarget;
        float k = _gainCoefficient;
        int writtenQuiet = _writtenQuiet;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (center != centerTarget)
                center = DspMath.Approach(center, centerTarget, delayCoefficient, DelaySnap);
            if (amplitude != amplitudeTarget)
                amplitude = DspMath.Approach(amplitude, amplitudeTarget, delayCoefficient, DelaySnap);
            if (feedback != feedbackTarget)
                feedback = DspMath.Approach(feedback, feedbackTarget, k, GainSnap);
            if (dry != dryTarget)
                dry = DspMath.Approach(dry, dryTarget, k, GainSnap);
            if (wet != wetTarget)
                wet = DspMath.Approach(wet, wetTarget, k, GainSnap);

            float delayLeft = Math.Clamp(center + amplitude * lfoLeft, MinDelaySamples, maxDelay);
            float delayRight = Math.Clamp(center + amplitude * lfoRight, MinDelaySamples, maxDelay);
            lfoLeft += lfoLeftStep;
            lfoRight += lfoRightStep;

            float readLeft = Read(left, mask, write, delayLeft);
            float readRight = Read(right, mask, write, delayRight);

            StereoSample x = buffer[i];
            float intoLeft = DspMath.Flush(x.left + feedback * readLeft);
            float intoRight = DspMath.Flush(x.right + feedback * readRight);
            left[write] = intoLeft;
            right[write] = intoRight;
            write = (write + 1) & mask;
            writtenQuiet = MathF.Max(MathF.Abs(intoLeft), MathF.Abs(intoRight)) > SilenceFloor ? 0 : Math.Min(QuietForever, writtenQuiet + 1);

            buffer[i] = new StereoSample(x.left * dry + readLeft * wet, x.right * dry + readRight * wet);
        }

        _write = write;
        _lfoLeft = lfoLeft;
        _lfoRight = lfoRight;
        _center = center;
        _amplitude = amplitude;
        _feedback = feedback;
        _dry = dry;
        _wet = wet;
        _writtenQuiet = writtenQuiet;
    }

    // Catmull-Rom through the four samples around the point `delay` samples before the one about to be written.
    // A delay of at least 2 keeps all four in the past.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Read(float[] line, int mask, int write, float delay)
    {
        int whole = (int)delay;
        float t = 1f - (delay - whole);
        int at = write - whole - 1;
        float y0 = line[(at - 1) & mask];
        float y1 = line[at & mask];
        float y2 = line[(at + 1) & mask];
        float y3 = line[(at + 2) & mask];
        float c1 = 0.5f * (y2 - y0);
        float c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
        float c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * t + c2) * t + c1) * t + y1;
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
