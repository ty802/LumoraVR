// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A distortion: drive into a transfer curve, a DC blocker, a tone low-pass, a level, crossfaded against the dry
// signal.
//
// The curve runs at twice or four times the mixer's rate. Going up is a half-band interpolator per doubling and
// coming down a half-band decimator; each half-band passes the audible band within a thousandth of a decibel and
// stops everything that would fold back into it by 80 dB, so harmonics the curve makes between the audible band
// and the raised rate are filtered away instead of folding back down as the inharmonic hash a naive clipper has.
// Four times is two doublings, the second with a much shorter filter because its transition band is wide.
//
// Every oversampling choice runs exactly 42 samples late: the two-times path is padded by 9, the plain path by all
// 42, and the four-times path carries one extra sample at the doubled rate to make its 41.5 whole. The dry signal
// is delayed to match, so the mix is phase-aligned at every setting, and switching oversampling or curve can
// crossfade between old and new without a jump. Drive, level and mix glide per sample, the tone corner in
// octaves. -xlinka
public sealed class DistortionFilter : IAudioDspFilter
{
    public const int Latency = 42;

    private const int TwoTimesPad = 9;
    private const int Chunk = 256;
    private const int ControlBlock = 32;
    private const int RingSize = 64;
    private const double GainSeconds = 0.02;
    private const double SwitchSeconds = 0.01;
    private const double ToneSeconds = 0.03;
    private const double DcBlockHz = 10.0;
    private const float GainSnap = 1e-6f;
    private const float TubeBias = 0.25f;

    private static readonly float[] FirstStage = Halfband.Design(67, 80.0);
    private static readonly float[] SecondStage = Halfband.Design(35, 80.0);
    private static readonly float TubeOffset = MathF.Tanh(TubeBias);
    private static readonly float TubeScale = 1f / (1f + MathF.Tanh(TubeBias));

    private sealed class Pipeline
    {
        public readonly HalfbandUpsampler FirstUp = new(FirstStage);
        public readonly HalfbandDownsampler FirstDown = new(FirstStage);
        public readonly HalfbandUpsampler SecondUp = new(SecondStage);
        public readonly HalfbandDownsampler SecondDown = new(SecondStage);
        public float Held;

        public void Clear()
        {
            FirstUp.Clear();
            FirstDown.Clear();
            SecondUp.Clear();
            SecondDown.Clear();
            Held = 0f;
        }
    }

    private readonly StagedSettings<DistortionSettings> _settings;
    private readonly Pipeline _twoLeft = new();
    private readonly Pipeline _twoRight = new();
    private readonly Pipeline _fourLeft = new();
    private readonly Pipeline _fourRight = new();
    private readonly float[] _ringLeft = new float[RingSize];
    private readonly float[] _ringRight = new float[RingSize];
    private readonly StereoSample[] _wetScratch = new StereoSample[Chunk];
    private readonly StereoSample[] _dryScratch = new StereoSample[Chunk];
    private readonly SvfChain _tone = new(1);
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;
    private int _sampleRate;
    private int _ringWrite;

    private DistortionCurve _curve;
    private DistortionCurve _curveTarget;
    private DistortionCurve _fromCurve;
    private float _curveFade = 1f;
    private DistortionOversampling _mode;
    private DistortionOversampling _modeTarget;
    private DistortionOversampling _fromMode;
    private float _modeFade = 1f;
    private float _switchStep;

    private float _drive = 1f;
    private float _driveTarget = 1f;
    private float _level = 1f;
    private float _levelTarget = 1f;
    private float _dry;
    private float _dryTarget;
    private float _wet = 1f;
    private float _wetTarget = 1f;
    private float _gainCoefficient;
    private double _toneGlide;
    private float _toneMorph;
    private int _countdown;

    private float _dcLeft;
    private float _dcRight;
    private float _dcGain;
    private bool _primed;

    public DistortionFilter()
        : this(AudioSimulator.DefaultSampleRate, DistortionSettings.Default)
    {
    }

    public DistortionFilter(int sampleRate, in DistortionSettings settings)
    {
        _settings = new StagedSettings<DistortionSettings>(settings.Clamped());
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public DistortionSettings Settings
    {
        get => _settings.Value;
        set => _settings.Write(value.Clamped());
    }

    public int SampleRate => _sampleRate;

    public int LatencySamples => Latency;

    public void Reset() => _resetRequested = true;

    // The curve itself, at unity drive.
    public static float Shape(float x, DistortionCurve curve)
    {
        switch (curve)
        {
            case DistortionCurve.HardClip:
                return Math.Clamp(x, -1f, 1f);
            case DistortionCurve.Tube:
                return (DspMath.Tanh(x + TubeBias) - TubeOffset) * TubeScale;
            case DistortionCurve.Foldback:
            {
                // A triangle through the origin with slope 1: past full scale the wave reflects back inward.
                float t = (x + 1f) * 0.25f;
                t -= MathF.Floor(t);
                return 1f - MathF.Abs(4f * t - 2f);
            }
            default:
                return DspMath.Tanh(x);
        }
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
        for (int offset = 0; offset < buffer.Length; offset += Chunk)
        {
            int count = Math.Min(Chunk, buffer.Length - offset);
            RenderChunk(buffer.Slice(offset, count));
        }
    }

    private void RenderChunk(Span<StereoSample> buffer)
    {
        int count = buffer.Length;
        Span<StereoSample> wet = _wetScratch.AsSpan(0, count);
        Span<StereoSample> dry = _dryScratch.AsSpan(0, count);

        if (_curveFade >= 1f && _curve != _curveTarget)
        {
            _fromCurve = _curve;
            _curve = _curveTarget;
            _curveFade = 0f;
        }
        if (_modeFade >= 1f && _mode != _modeTarget)
        {
            _fromMode = _mode;
            _mode = _modeTarget;
            _modeFade = 0f;
            PipelinesFor(_mode, out Pipeline? left, out Pipeline? right);
            left?.Clear();
            right?.Clear();
        }

        float[] ringLeft = _ringLeft;
        float[] ringRight = _ringRight;
        int write = _ringWrite;
        float drive = _drive;
        float driveTarget = _driveTarget;
        float k = _gainCoefficient;
        float dcGain = _dcGain;
        float dcLeft = _dcLeft;
        float dcRight = _dcRight;

        for (int i = 0; i < count; i++)
        {
            StereoSample x = buffer[i];
            ringLeft[write] = x.left;
            ringRight[write] = x.right;
            int late = (write - Latency) & (RingSize - 1);
            dry[i] = new StereoSample(ringLeft[late], ringRight[late]);

            if (drive != driveTarget)
                drive = DspMath.Approach(drive, driveTarget, k, GainSnap);

            float curveT = _curveFade;
            float left = Run(_mode, true, write, drive, curveT);
            float right = Run(_mode, false, write, drive, curveT);
            if (_modeFade < 1f)
            {
                float oldLeft = Run(_fromMode, true, write, drive, curveT);
                float oldRight = Run(_fromMode, false, write, drive, curveT);
                float t = _modeFade;
                left = oldLeft + (left - oldLeft) * t;
                right = oldRight + (right - oldRight) * t;
                t += _switchStep;
                if (t >= 1f)
                {
                    t = 1f;
                    PipelinesFor(_fromMode, out Pipeline? oldPipelineLeft, out Pipeline? oldPipelineRight);
                    oldPipelineLeft?.Clear();
                    oldPipelineRight?.Clear();
                }
                _modeFade = t;
            }
            if (curveT < 1f)
                _curveFade = MathF.Min(1f, curveT + _switchStep);

            // The asymmetric curve leaves DC behind; a 10 Hz trapezoidal one-pole takes it off every curve alike.
            float v = (left - dcLeft) * dcGain;
            float lowLeft = v + dcLeft;
            dcLeft = DspMath.Flush(lowLeft + v);
            float w = (right - dcRight) * dcGain;
            float lowRight = w + dcRight;
            dcRight = DspMath.Flush(lowRight + w);
            wet[i] = new StereoSample(left - lowLeft, right - lowRight);

            write = (write + 1) & (RingSize - 1);
        }

        _ringWrite = write;
        _drive = drive;
        _dcLeft = dcLeft;
        _dcRight = dcRight;

        int done = 0;
        while (done < count)
        {
            if (_countdown <= 0)
            {
                _tone.Control(_sampleRate, _toneGlide, _toneMorph, ControlBlock);
                _countdown = ControlBlock;
            }
            int run = Math.Min(_countdown, count - done);
            _tone.Run(wet.Slice(done, run));
            _countdown -= run;
            done += run;
        }

        float level = _level;
        float levelTarget = _levelTarget;
        float dryGain = _dry;
        float dryTarget = _dryTarget;
        float wetGain = _wet;
        float wetTarget = _wetTarget;
        for (int i = 0; i < count; i++)
        {
            if (level != levelTarget)
                level = DspMath.Approach(level, levelTarget, k, GainSnap);
            if (dryGain != dryTarget)
                dryGain = DspMath.Approach(dryGain, dryTarget, k, GainSnap);
            if (wetGain != wetTarget)
                wetGain = DspMath.Approach(wetGain, wetTarget, k, GainSnap);
            float wetScale = level * wetGain;
            StereoSample d = dry[i];
            StereoSample s = wet[i];
            buffer[i] = new StereoSample(d.left * dryGain + s.left * wetScale, d.right * dryGain + s.right * wetScale);
        }
        _level = level;
        _dry = dryGain;
        _wet = wetGain;
    }

    // One base-rate sample through one oversampling path for one side, read from the input ring at that path's pad.
    private float Run(DistortionOversampling mode, bool leftSide, int write, float drive, float curveT)
    {
        float[] ring = leftSide ? _ringLeft : _ringRight;
        switch (mode)
        {
            case DistortionOversampling.Four:
            {
                Pipeline p = leftSide ? _fourLeft : _fourRight;
                p.FirstUp.Process(ring[write] * drive, out float a, out float b);
                float held = p.Held;
                p.Held = b;
                p.SecondUp.Process(held, out float p0, out float p1);
                float e0 = p.SecondDown.Process(Curve(p0, curveT), Curve(p1, curveT));
                p.SecondUp.Process(a, out float q0, out float q1);
                float e1 = p.SecondDown.Process(Curve(q0, curveT), Curve(q1, curveT));
                return p.FirstDown.Process(e0, e1);
            }
            case DistortionOversampling.Two:
            {
                Pipeline p = leftSide ? _twoLeft : _twoRight;
                p.FirstUp.Process(ring[(write - TwoTimesPad) & (RingSize - 1)] * drive, out float a, out float b);
                return p.FirstDown.Process(Curve(a, curveT), Curve(b, curveT));
            }
            default:
                return Curve(ring[(write - Latency) & (RingSize - 1)] * drive, curveT);
        }
    }

    private float Curve(float x, float t)
    {
        float shaped = Shape(x, _curve);
        if (t >= 1f)
            return shaped;
        float old = Shape(x, _fromCurve);
        return old + (shaped - old) * t;
    }

    private void PipelinesFor(DistortionOversampling mode, out Pipeline? left, out Pipeline? right)
    {
        left = mode == DistortionOversampling.Four ? _fourLeft : mode == DistortionOversampling.Two ? _twoLeft : null;
        right = mode == DistortionOversampling.Four ? _fourRight : mode == DistortionOversampling.Two ? _twoRight : null;
    }

    private void Retarget()
    {
        ref readonly DistortionSettings s = ref _settings.Current;
        _curveTarget = s.Curve;
        _modeTarget = s.Oversampling;
        _driveTarget = DspMath.DbToGain(s.Drive);
        _levelTarget = DspMath.DbToGain(s.Level);
        DspMath.Crossfade(s.Mix, out _dryTarget, out _wetTarget);
        _tone.SetTarget(BiquadFilterType.LowPass, 1, s.Tone, BiquadSettings.DefaultQ, 0.0);
    }

    private void Snap()
    {
        _curve = _curveTarget;
        _fromCurve = _curveTarget;
        _curveFade = 1f;
        _mode = _modeTarget;
        _fromMode = _modeTarget;
        _modeFade = 1f;
        _drive = _driveTarget;
        _level = _levelTarget;
        _dry = _dryTarget;
        _wet = _wetTarget;
        _tone.Snap(_sampleRate);
        _countdown = 0;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        _gainCoefficient = DspMath.OnePole(GainSeconds, sampleRate);
        _switchStep = (float)(1.0 / (SwitchSeconds * sampleRate));
        _toneGlide = DspMath.OnePole(ToneSeconds, sampleRate, ControlBlock);
        _toneMorph = (float)(ControlBlock / (SwitchSeconds * sampleRate));
        double g = Math.Tan(Math.PI * DcBlockHz / sampleRate);
        _dcGain = (float)(g / (1.0 + g));
        ClearState();
    }

    private void ClearState()
    {
        _twoLeft.Clear();
        _twoRight.Clear();
        _fourLeft.Clear();
        _fourRight.Clear();
        Array.Clear(_ringLeft);
        Array.Clear(_ringRight);
        _ringWrite = 0;
        _dcLeft = 0f;
        _dcRight = 0f;
        Retarget();
        Snap();
    }
}
