// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A noise gate with a range, stereo-linked. Any sample on either channel at or above the threshold opens it and
// restarts the hold; once the hold has run out with nothing over the threshold it closes. The gain follows with
// one-poles in linear gain, the attack one on the way up to unity and the release one on the way down to the
// range, and lands exactly on each, so an open gate hands the signal back bit for bit. A range of 0 dB is a gate
// that never shuts. A change of range glides, so a gate pulled open by its setting while shut does not snap open
// at attack speed. -xlinka
public sealed class GateFilter : IAudioDspFilter
{
    private const double RangeSeconds = 0.02;
    private const float GainSnap = 1e-7f;

    private readonly StagedSettings<GateSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private float _threshold;
    private int _holdSamples;
    private int _holdLeft;
    private float _attack;
    private float _release;
    private float _range;
    private float _rangeTarget;
    private float _rangeCoefficient;
    private float _gain;
    private bool _primed;

    public GateFilter()
        : this(AudioSimulator.DefaultSampleRate, GateSettings.Default)
    {
    }

    public GateFilter(int sampleRate, in GateSettings settings)
    {
        _settings = new StagedSettings<GateSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public GateSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    // Audio thread: whether the gate is open or holding.
    public bool IsOpen => _holdLeft > 0;

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
                ClearState();
        }
        _primed = true;

        SampleBuffers.Sanitize(buffer);
        float threshold = _threshold;
        int holdSamples = _holdSamples;
        int holdLeft = _holdLeft;
        float attack = _attack;
        float release = _release;
        float range = _range;
        float rangeTarget = _rangeTarget;
        float rangeCoefficient = _rangeCoefficient;
        float gain = _gain;

        for (int i = 0; i < buffer.Length; i++)
        {
            StereoSample x = buffer[i];
            if (range != rangeTarget)
                range = DspMath.Approach(range, rangeTarget, rangeCoefficient, GainSnap);

            // The +1 makes an open gate count the sample that opened it, so it stays open for exactly the hold
            // after the last sample over the threshold.
            if (MathF.Max(MathF.Abs(x.left), MathF.Abs(x.right)) >= threshold)
                holdLeft = holdSamples + 1;
            else if (holdLeft > 0)
                holdLeft--;

            float target = holdLeft > 0 ? 1f : range;
            if (gain != target)
                gain = DspMath.Approach(gain, target, target > gain ? attack : release, GainSnap);
            if (gain != 1f)
                buffer[i] = x.Multiply(gain);
        }

        _holdLeft = holdLeft;
        _range = range;
        _gain = gain;
    }

    private void Retarget()
    {
        ref readonly GateSettings s = ref _settings.Current;
        double rate = _sampleRate;
        _threshold = DspMath.DbToGain(s.Threshold);
        _rangeTarget = DspMath.DbToGain(s.Range);
        _attack = DspMath.OnePole(s.Attack * 0.001, rate);
        _release = DspMath.OnePole(s.Release * 0.001, rate);
        _holdSamples = (int)Math.Round(s.Hold * 0.001 * rate);
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _rangeCoefficient = DspMath.OnePole(RangeSeconds, sampleRate);
        ClearState();
    }

    // Shut, at the range, with nothing held.
    private void ClearState()
    {
        Retarget();
        _range = _rangeTarget;
        _gain = _range;
        _holdLeft = 0;
    }
}
