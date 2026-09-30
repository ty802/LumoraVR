// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Stereo width in mid and side: mid = (L + R) / 2 is left alone, side = (L - R) / 2 is scaled, and the two are put
// back together. Whatever the width, L + R comes out as it went in, so the effect vanishes when the result is
// summed to mono instead of cancelling anything. Width glides per sample, and resting at exactly 1 the buffer is
// handed back untouched.
public sealed class StereoWidthFilter : IAudioDspFilter
{
    private const double GainSeconds = 0.015;
    private const float GainSnap = 1e-6f;

    private readonly StagedSettings<StereoWidthSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;
    private float _width = 1f;
    private float _widthTarget = 1f;
    private float _coefficient;
    private bool _primed;

    public StereoWidthFilter()
        : this(AudioSimulator.DefaultSampleRate, StereoWidthSettings.Default)
    {
    }

    public StereoWidthFilter(int sampleRate, in StereoWidthSettings settings)
    {
        _settings = new StagedSettings<StereoWidthSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public StereoWidthSettings Settings
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
            _widthTarget = _settings.Current.Width;
            _width = _widthTarget;
        }
        if (_settings.Take())
        {
            _widthTarget = _settings.Current.Width;
            if (!_primed)
                _width = _widthTarget;
        }
        _primed = true;

        SampleBuffers.Sanitize(buffer);
        float width = _width;
        float target = _widthTarget;
        if (width == 1f && target == 1f)
            return;
        float k = _coefficient;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (width != target)
                width = DspMath.Approach(width, target, k, GainSnap);
            StereoSample x = buffer[i];
            float mid = 0.5f * (x.left + x.right);
            float side = 0.5f * (x.left - x.right) * width;
            buffer[i] = new StereoSample(mid + side, mid - side);
        }
        _width = width;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _coefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _widthTarget = _settings.Current.Width;
        _width = _widthTarget;
    }
}
