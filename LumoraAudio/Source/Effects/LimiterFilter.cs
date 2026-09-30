// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A lookahead brickwall limiter, stereo-linked.
//
// For every sample the gain it would need to sit exactly at the ceiling is worked out as it arrives. The gain
// actually used is the smallest of those needs over the last lookahead + 1 samples, let back up toward 1 by a
// release one-pole, then averaged over the same lookahead + 1 samples, while the sound itself runs lookahead
// samples behind. The proof that nothing gets through is short: a peak leaves the delay exactly when the average
// covers the lookahead + 1 samples since it arrived, every one of those minimums includes the peak's own need,
// and the release can only sit below a minimum, never above it. So the average is at or under the need and the
// peak comes out at or under the ceiling, having been ramped into over the whole lookahead rather than clipped.
//
// A clamp at the ceiling sits on the output anyway. With the ceiling standing still it only ever trims float
// rounding; when the ceiling is pulled down faster than the lookahead can follow it is what holds the line.
// Lookahead is fixed at 5 ms and is the latency (LatencySamples). -xlinka
public sealed class LimiterFilter : IAudioDspFilter, IAudioTailFilter
{
    private const double LookaheadSeconds = 0.005;
    private const double GainSeconds = 0.02;
    private const float GainSnap = 1e-7f;
    private const float ReleaseSnap = 1e-7f;
    private const float SilenceFloor = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private readonly StagedSettings<LimiterSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private int _lookahead;
    private int _window;
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();
    private float[] _box = Array.Empty<float>();
    private double _boxSum;
    private int _write;

    // The sliding minimum: a deque of needs in rising order with the sample each arrived on.
    private float[] _minValue = Array.Empty<float>();
    private long[] _minIndex = Array.Empty<long>();
    private int _minHead;
    private int _minCount;
    private long _sampleNumber;

    private float _released = 1f;
    private float _releaseCoefficient;
    private float _inputGain = 1f;
    private float _inputGainTarget = 1f;
    private float _ceiling = 1f;
    private float _ceilingTarget = 1f;
    private float _glideCoefficient;
    private bool _primed;
    private int _quietSamples = QuietForever;
    private long _clamped;

    public LimiterFilter()
        : this(AudioSimulator.DefaultSampleRate, LimiterSettings.Default)
    {
    }

    public LimiterFilter(int sampleRate, in LimiterSettings settings)
    {
        _settings = new StagedSettings<LimiterSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public LimiterSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public int LatencySamples => _lookahead;

    public bool HasTail => _quietSamples < _window;

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

        // A running sum drifts; the window is short enough to add up again once a block.
        double sum = 0.0;
        for (int i = 0; i < _box.Length; i++)
            sum += _box[i];
        _boxSum = sum;

        float[] left = _left;
        float[] right = _right;
        float[] box = _box;
        float[] minValue = _minValue;
        long[] minIndex = _minIndex;
        int window = _window;
        double inverseWindow = 1.0 / window;
        int write = _write;
        int head = _minHead;
        int count = _minCount;
        long n = _sampleNumber;
        float released = _released;
        float release = _releaseCoefficient;
        float inputGain = _inputGain;
        float inputGainTarget = _inputGainTarget;
        float ceiling = _ceiling;
        float ceilingTarget = _ceilingTarget;
        float glide = _glideCoefficient;
        double boxSum = _boxSum;
        long clamped = _clamped;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (inputGain != inputGainTarget)
                inputGain = DspMath.Approach(inputGain, inputGainTarget, glide, GainSnap);
            if (ceiling != ceilingTarget)
                ceiling = DspMath.Approach(ceiling, ceilingTarget, glide, GainSnap);

            StereoSample x = buffer[i];
            float l = x.left * inputGain;
            float r = x.right * inputGain;
            float level = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            float need = level > ceiling ? ceiling / level : 1f;

            while (count > 0)
            {
                int back = head + count - 1;
                if (back >= window)
                    back -= window;
                if (minValue[back] < need)
                    break;
                count--;
            }
            while (count > 0 && minIndex[head] <= n - window)
            {
                if (++head == window)
                    head = 0;
                count--;
            }
            int slot = head + count;
            if (slot >= window)
                slot -= window;
            minValue[slot] = need;
            minIndex[slot] = n;
            count++;
            float least = minValue[head];

            if (least < released)
                released = least;
            else if (released != least)
            {
                released += (least - released) * release;
                if (least - released < ReleaseSnap)
                    released = least;
            }

            // In double from the start: a float difference rounds, and 241 of them add up to enough to let a peak
            // through by a few parts in a million.
            boxSum += (double)released - box[write];
            box[write] = released;
            float gain = (float)(boxSum * inverseWindow);

            left[write] = l;
            right[write] = r;
            int read = write + 1 == window ? 0 : write + 1;
            float outLeft = left[read] * gain;
            float outRight = right[read] * gain;
            if (++write == window)
                write = 0;
            n++;

            float louder = MathF.Max(MathF.Abs(outLeft), MathF.Abs(outRight));
            if (louder > ceiling)
            {
                if (louder > ceiling * 1.000001f)
                    clamped++;
                outLeft = Math.Clamp(outLeft, -ceiling, ceiling);
                outRight = Math.Clamp(outRight, -ceiling, ceiling);
            }
            buffer[i] = new StereoSample(outLeft, outRight);
        }

        _write = write;
        _minHead = head;
        _minCount = count;
        _sampleNumber = n;
        _released = released;
        _inputGain = inputGain;
        _ceiling = ceiling;
        _boxSum = boxSum;
        _clamped = clamped;
    }

    private void Retarget()
    {
        ref readonly LimiterSettings s = ref _settings.Current;
        _inputGainTarget = DspMath.DbToGain(s.InputGain);
        _ceilingTarget = DspMath.DbToGain(s.Ceiling);
        _releaseCoefficient = DspMath.OnePole(s.Release * 0.001, _sampleRate);
    }

    private void Snap()
    {
        _inputGain = _inputGainTarget;
        _ceiling = _ceilingTarget;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _lookahead = Math.Max(1, (int)Math.Round(LookaheadSeconds * sampleRate));
        _window = _lookahead + 1;
        _left = new float[_window];
        _right = new float[_window];
        _box = new float[_window];
        _minValue = new float[_window];
        _minIndex = new long[_window];
        _glideCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        ClearState();
    }

    private void ClearState()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        Array.Fill(_box, 1f);
        _boxSum = _window;
        _write = 0;
        _minHead = 0;
        _minCount = 0;
        _sampleNumber = 0;
        _released = 1f;
        _quietSamples = QuietForever;
        Retarget();
        Snap();
    }
}
