// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// An auto-pan: a constant-power balance swung by a sine. At pan p in [-1, 1] the left side is scaled by
// sqrt(2) cos((p + 1) pi / 4) and the right by sqrt(2) sin((p + 1) pi / 4), which is unity on both in the middle
// and keeps the summed power of a mono source the same all the way across, so it moves without pumping. The gains
// are worked out exactly once per control block and followed in straight lines between, depth glides at control
// rate, and a new rate glides in octaves from the phase it is at. -xlinka
public sealed class AutoPanFilter : IAudioDspFilter
{
    private const int ControlBlock = 32;
    private const double DepthSeconds = 0.015;

    private readonly StagedSettings<AutoPanSettings> _settings;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;

    private double _rateOctave;
    private double _rateOctaveTarget;
    private double _phase;
    private double _depth;
    private double _depthTarget;
    private double _depthCoefficient;
    private float _gainLeft = 1f;
    private float _gainRight = 1f;
    private float _stepLeft;
    private float _stepRight;
    private float _endLeft = 1f;
    private float _endRight = 1f;
    private int _countdown;
    private bool _primed;

    public AutoPanFilter()
        : this(AudioSimulator.DefaultSampleRate, AutoPanSettings.Default)
    {
    }

    public AutoPanFilter(int sampleRate, in AutoPanSettings settings)
    {
        _settings = new StagedSettings<AutoPanSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public AutoPanSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public void Reset() => _resetRequested = true;

    // The gains for a pan position, unity on both sides at 0.
    public static void Gains(double pan, out float left, out float right)
    {
        double angle = (pan + 1.0) * (Math.PI * 0.25);
        left = (float)(Math.Sqrt(2.0) * Math.Cos(angle));
        right = (float)(Math.Sqrt(2.0) * Math.Sin(angle));
    }

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
        if (_depth == 0.0 && _depthTarget == 0.0 && _gainLeft == 1f && _gainRight == 1f)
            return;

        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_countdown <= 0)
            {
                DspMath.Glide(ref _depth, _depthTarget, _depthCoefficient, 1e-6);
                DspMath.Glide(ref _rateOctave, _rateOctaveTarget, _depthCoefficient, 1e-6);
                double end = _phase + Math.Pow(2.0, _rateOctave) * ControlBlock / _sampleRate;
                end -= Math.Floor(end);
                _phase = end;
                Gains(_depth * Math.Sin(2.0 * Math.PI * end), out _endLeft, out _endRight);
                _stepLeft = (_endLeft - _gainLeft) * (1f / ControlBlock);
                _stepRight = (_endRight - _gainRight) * (1f / ControlBlock);
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, buffer.Length - offset);
            float gainLeft = _gainLeft;
            float gainRight = _gainRight;
            float stepLeft = _stepLeft;
            float stepRight = _stepRight;
            Span<StereoSample> slice = buffer.Slice(offset, run);
            for (int i = 0; i < slice.Length; i++)
            {
                gainLeft += stepLeft;
                gainRight += stepRight;
                slice[i] = slice[i].Scale(gainLeft, gainRight);
            }
            _countdown -= run;
            offset += run;
            // Lands on the block's end values exactly, so a pan glided back to the middle is unity again.
            _gainLeft = _countdown == 0 ? _endLeft : gainLeft;
            _gainRight = _countdown == 0 ? _endRight : gainRight;
        }
    }

    private void Retarget()
    {
        ref readonly AutoPanSettings s = ref _settings.Current;
        _rateOctaveTarget = Math.Log2(s.Rate);
        _depthTarget = s.Depth;
    }

    private void Snap()
    {
        _depth = _depthTarget;
        _rateOctave = _rateOctaveTarget;
        Gains(_depth * Math.Sin(2.0 * Math.PI * _phase), out _gainLeft, out _gainRight);
        _endLeft = _gainLeft;
        _endRight = _gainRight;
        _stepLeft = 0f;
        _stepRight = 0f;
        _countdown = 0;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _depthCoefficient = DspMath.OnePole(DepthSeconds, sampleRate, ControlBlock);
        ClearState();
    }

    private void ClearState()
    {
        _phase = 0.0;
        Retarget();
        Snap();
    }
}
