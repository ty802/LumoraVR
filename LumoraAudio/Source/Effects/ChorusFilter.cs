// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A stereo chorus: each side is a copy of itself read through a delay swinging either side of a centre in the
// tens of milliseconds, crossfaded against the dry signal. The sweep, the interpolation and every glide are the
// ModulatedDelay engine's. Settings reach the audio thread as an immutable box swapped by reference and Process
// allocates nothing unless the sample rate changes.
public sealed class ChorusFilter : IAudioDspFilter, IAudioTailFilter
{
    private readonly StagedSettings<ChorusSettings> _settings;
    private readonly ModulatedDelay _engine = new();
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;
    private bool _primed;

    public ChorusFilter()
        : this(AudioSimulator.DefaultSampleRate, ChorusSettings.Default)
    {
    }

    public ChorusFilter(int sampleRate, in ChorusSettings settings)
    {
        _settings = new StagedSettings<ChorusSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public ChorusSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public bool HasTail => _engine.HasTail;

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
            _engine.Clear();
        }
        if (_settings.Take())
        {
            Retarget();
            if (!_primed)
                _engine.Snap();
        }
        _primed = true;
        SampleBuffers.Sanitize(buffer);
        _engine.Process(buffer);
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _engine.Configure(sampleRate, ChorusSettings.MaxDelay + ChorusSettings.MaxDepth);
        Retarget();
        _engine.Snap();
    }

    private void Retarget()
    {
        ref readonly ChorusSettings s = ref _settings.Current;
        _engine.SetTargets(s.Rate, s.Delay, s.Depth, s.Feedback, 0.5 * s.Spread, s.Mix);
    }
}
