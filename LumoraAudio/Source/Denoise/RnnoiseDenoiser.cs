// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// denoise.c: Copyright (c) 2024 Jean-Marc Valin, Copyright (c) 2018 Gregor Richards,
// Copyright (c) 2017 Mozilla.
// See LICENSE-RNNoise.txt.

using System;

namespace Lumora.Audio;

// RNNoise v0.2 with its LITTLE model: a managed port of rnnoise_process_frame that reproduces
// the C library's x86 default build (SSE2, int8 GRUs) bit for bit where SSE is available.
// Frames are 480 samples of 48 kHz mono. Output lags input by 960 samples: half a window of
// overlap-add plus the frame RNNoise holds back for its pitch filter.
// One instance per stream, not thread-safe. The model loads once and is shared; nothing
// allocates after construction. -xlinka
public sealed class RnnoiseDenoiser
{
    public const int FrameSize = 480;
    public const int SampleRate = 48000;

    private const int WindowSize = 2 * FrameSize;
    private const int FreqSize = FrameSize + 1;
    private const int Bands = 32;
    private const int FeatureCount = 2 * Bands + 1;

    // NaN, infinite or absurdly large samples would put NaN into the recurrent state for good.
    // NaN reads as 0 and magnitudes are held to this, far past anything 16-bit audio reaches.
    private const float InputLimit = 1e9f;

    // rnn_biquad coefficients of the input high-pass, stored as float like denoise.c does.
    internal const float HpB0 = -2f;
    internal const float HpB1 = 1f;
    internal const float HpA0 = (float)-1.99599;
    internal const float HpA1 = (float)0.99600;

    // eband20ms: 32 bands over 50 Hz bins up to 20 kHz; bins past 400 get zero gain.
    private static ReadOnlySpan<int> BandEdges =>
    [
        0, 2, 4, 6, 8, 10, 12, 15, 18, 21, 24, 28, 32, 36, 41, 47, 53, 60, 68, 77, 87, 98, 110, 124,
        140, 157, 176, 198, 223, 251, 282, 317, 356, 400,
    ];

    private static readonly double DctScale = Math.Sqrt(2.0 / 22);

    private readonly RnnoiseModel _model;
    private readonly KissFft _fft;
    private readonly RnnoiseNetwork _network;
    private readonly RnnoisePitchScratch _pitchScratch = new();

    // DenoiseState
    private readonly float[] _analysisMem = new float[FrameSize];
    private readonly float[] _synthesisMem = new float[FrameSize];
    private readonly float[] _pitchBuf = new float[RnnoisePitch.BufferSize];
    private float _lastGain;
    private int _lastPeriod;
    private float _memHp0;
    private float _memHp1;
    private readonly float[] _lastg = new float[Bands];
    private readonly KissCpx[] _delayedX = new KissCpx[FreqSize];
    private readonly KissCpx[] _delayedP = new KissCpx[FreqSize];
    private readonly float[] _delayedEx = new float[Bands];
    private readonly float[] _delayedEp = new float[Bands];
    private readonly float[] _delayedExp = new float[Bands];

    // Per-frame scratch.
    private readonly float[] _x = new float[FrameSize];
    private readonly KissCpx[] _spectrum = new KissCpx[FreqSize];
    private readonly KissCpx[] _pitchSpectrum = new KissCpx[FreqSize];
    private readonly float[] _ex = new float[Bands];
    private readonly float[] _ep = new float[Bands];
    private readonly float[] _exp = new float[Bands];
    private readonly float[] _features = new float[FeatureCount];
    private readonly float[] _gains = new float[Bands];
    private readonly float[] _bandGain = new float[Bands];
    private readonly float[] _ly = new float[Bands];
    private readonly float[] _bandSum = new float[Bands + 2];
    private readonly float[] _window = new float[WindowSize];
    private readonly KissCpx[] _fftIn = new KissCpx[WindowSize];
    private readonly KissCpx[] _fftOut = new KissCpx[WindowSize];
    private readonly float[] _pitchDown = new float[RnnoisePitch.BufferSize >> 1];
    private readonly float[] _r = new float[Bands];
    private readonly float[] _norm = new float[Bands];
    private readonly float[] _newE = new float[Bands];

    // Per-bin gains. InterpBandGain never writes past bin 399, so those stay 0 as the C code's
    // zero-initialised locals do.
    private readonly float[] _binGain = new float[FreqSize];
    private readonly float[] _rf = new float[FreqSize];
    private readonly float[] _normf = new float[FreqSize];

    private bool _ranNetwork;

    public RnnoiseDenoiser()
    {
        _model = RnnoiseModel.Shared;
        _fft = _model.Fft;
        _network = new RnnoiseNetwork(_model);
    }

    // Raw network output and features of the last frame, for the comparison harness.
    internal bool LastFrameRanNetwork => _ranNetwork;
    internal ReadOnlySpan<float> LastGains => _gains;
    internal ReadOnlySpan<float> LastFeatures => _features;

    public void Reset()
    {
        Array.Clear(_analysisMem);
        Array.Clear(_synthesisMem);
        Array.Clear(_pitchBuf);
        _lastGain = 0;
        _lastPeriod = 0;
        _memHp0 = 0;
        _memHp1 = 0;
        Array.Clear(_lastg);
        Array.Clear(_delayedX);
        Array.Clear(_delayedP);
        Array.Clear(_delayedEx);
        Array.Clear(_delayedEp);
        Array.Clear(_delayedExp);
        Array.Clear(_gains);
        Array.Clear(_features);
        _ranNetwork = false;
        _network.Reset();
    }

    // Denoises FrameSize samples in the 16-bit range the C library takes (about -32768..32767,
    // not -1..1). input and output may be the same span. Returns the voice probability, 0..1.
    public float ProcessFrame(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length < FrameSize)
            throw new ArgumentException($"Need {FrameSize} input samples, got {input.Length}.", nameof(input));
        if (output.Length < FrameSize)
            throw new ArgumentException($"Need room for {FrameSize} output samples, got {output.Length}.", nameof(output));

        HighPass(input);
        bool silence = ComputeFrameFeatures();
        _ranNetwork = !silence;
        float vad = 0;
        if (!silence)
        {
            vad = _network.Compute(_features, _gains);
            PitchFilter();
            for (int i = 0; i < Bands; i++)
            {
                // Decay capped at 0.6 per frame (RT60 of 135 ms). The gain kept for the next
                // frame is compensated for the energy change so a transient cannot leak noise.
                float g = _gains[i];
                float floor = .6f * _lastg[i];
                g = g > floor ? g : floor;
                _bandGain[i] = g;
                double compensated = g * (_delayedEx[i] + 1e-3) / (_ex[i] + 1e-3);
                _lastg[i] = (float)(1f < compensated ? 1f : compensated);
            }
            InterpBandGain(_binGain, _bandGain);
            for (int i = 0; i < FreqSize; i++)
            {
                _delayedX[i].R *= _binGain[i];
                _delayedX[i].I *= _binGain[i];
            }
        }
        FrameSynthesis(output);

        Array.Copy(_spectrum, _delayedX, FreqSize);
        Array.Copy(_pitchSpectrum, _delayedP, FreqSize);
        Array.Copy(_ex, _delayedEx, Bands);
        Array.Copy(_ep, _delayedEp, Bands);
        Array.Copy(_exp, _delayedExp, Bands);
        return vad;
    }

    // rnn_biquad with b = {-2, 1}, a = {-1.99599, 0.996}: DC removal, state updated in double.
    private void HighPass(ReadOnlySpan<float> input)
    {
        float mem0 = _memHp0;
        float mem1 = _memHp1;
        for (int i = 0; i < FrameSize; i++)
        {
            float xi = input[i];
            if (!(MathF.Abs(xi) <= InputLimit))
                xi = float.IsNaN(xi) ? 0 : MathF.CopySign(InputLimit, xi);
            float yi = xi + mem0;
            mem0 = (float)(mem1 + (HpB0 * (double)xi - HpA0 * (double)yi));
            mem1 = (float)(HpB1 * (double)xi - HpA1 * (double)yi);
            _x[i] = yi;
        }
        _memHp0 = mem0;
        _memHp1 = mem1;
    }

    // rnn_compute_frame_features. Returns true for a silent frame, which skips the network.
    private bool ComputeFrameFeatures()
    {
        Span<float> window = _window;

        _analysisMem.CopyTo(window);
        _x.CopyTo(window.Slice(FrameSize));
        _x.CopyTo(_analysisMem, 0);
        ApplyWindow(window);
        ForwardTransform(_spectrum, window);
        ComputeBandEnergy(_ex, _spectrum);

        Array.Copy(_pitchBuf, FrameSize, _pitchBuf, 0, RnnoisePitch.BufferSize - FrameSize);
        _x.CopyTo(_pitchBuf, RnnoisePitch.BufferSize - FrameSize);
        RnnoisePitch.Downsample(_pitchBuf, _pitchDown, RnnoisePitch.BufferSize);
        int pitchIndex = RnnoisePitch.Search(_pitchDown.AsSpan(RnnoisePitch.MaxPeriod >> 1), _pitchDown,
            RnnoisePitch.FrameSize, RnnoisePitch.MaxPeriod - 3 * RnnoisePitch.MinPeriod, _pitchScratch);
        pitchIndex = RnnoisePitch.MaxPeriod - pitchIndex;
        float gain = RnnoisePitch.RemoveDoubling(_pitchDown, RnnoisePitch.MaxPeriod, RnnoisePitch.MinPeriod,
            RnnoisePitch.FrameSize, ref pitchIndex, _lastPeriod, _lastGain, _pitchScratch);
        _lastPeriod = pitchIndex;
        _lastGain = gain;

        _pitchBuf.AsSpan(RnnoisePitch.BufferSize - WindowSize - pitchIndex, WindowSize).CopyTo(window);
        ApplyWindow(window);
        ForwardTransform(_pitchSpectrum, window);
        ComputeBandEnergy(_ep, _pitchSpectrum);
        ComputeBandCorrelation(_exp, _spectrum, _pitchSpectrum);
        for (int i = 0; i < Bands; i++)
            _exp[i] = (float)(_exp[i] / Math.Sqrt(.001 + _ex[i] * _ep[i]));
        Dct(_features.AsSpan(Bands, Bands), _exp);
        _features[2 * Bands] = (float)(.01 * (pitchIndex - 300));

        float logMax = -2;
        float follow = -2;
        float energy = 0;
        for (int i = 0; i < Bands; i++)
        {
            float ly = (float)Math.Log10(1e-2 + _ex[i]);
            double inner = follow - 1.5 > ly ? follow - 1.5 : ly;
            float floorMax = logMax - 7;
            ly = (float)(floorMax > inner ? floorMax : inner);
            _ly[i] = ly;
            logMax = logMax > ly ? logMax : ly;
            follow = (float)(follow - 1.5 > ly ? follow - 1.5 : ly);
            energy += _ex[i];
        }
        if (energy < 0.04)
        {
            // No audio: leave the network state alone.
            Array.Clear(_features);
            return true;
        }
        Dct(_features.AsSpan(0, Bands), _ly);
        _features[0] -= 12;
        _features[1] -= 4;
        return false;
    }

    // rnn_pitch_filter on the delayed frame: mixes in the pitch-period spectrum per band by how
    // voiced the band is against its gain, then restores each band's original energy.
    private void PitchFilter()
    {
        KissCpx[] x = _delayedX;
        KissCpx[] p = _delayedP;
        for (int i = 0; i < Bands; i++)
        {
            float exp = _delayedExp[i];
            float g = _gains[i];
            float r;
            if (exp > g)
                r = 1;
            else
                r = (float)(exp * exp * (1 - g * g) / (.001 + g * g * (1 - exp * exp)));
            r = 0 > r ? 0 : r;
            r = 1 < r ? 1 : r;
            r = (float)Math.Sqrt(r);
            r = (float)(r * Math.Sqrt(_delayedEx[i] / (1e-8 + _delayedEp[i])));
            _r[i] = r;
        }
        InterpBandGain(_rf, _r);
        for (int i = 0; i < FreqSize; i++)
        {
            x[i].R += _rf[i] * p[i].R;
            x[i].I += _rf[i] * p[i].I;
        }
        ComputeBandEnergy(_newE, x);
        for (int i = 0; i < Bands; i++)
            _norm[i] = (float)Math.Sqrt(_delayedEx[i] / (1e-8 + _newE[i]));
        InterpBandGain(_normf, _norm);
        for (int i = 0; i < FreqSize; i++)
        {
            x[i].R *= _normf[i];
            x[i].I *= _normf[i];
        }
    }

    private void FrameSynthesis(Span<float> output)
    {
        Span<float> x = _window;
        InverseTransform(x, _delayedX);
        ApplyWindow(x);
        for (int i = 0; i < FrameSize; i++)
            output[i] = x[i] + _synthesisMem[i];
        x.Slice(FrameSize, FrameSize).CopyTo(_synthesisMem);
    }

    private void ApplyWindow(Span<float> x)
    {
        float[] w = _model.HalfWindow;
        for (int i = 0; i < FrameSize; i++)
        {
            x[i] *= w[i];
            x[WindowSize - 1 - i] *= w[i];
        }
    }

    private void ForwardTransform(KissCpx[] output, ReadOnlySpan<float> input)
    {
        KissCpx[] x = _fftIn;
        for (int i = 0; i < WindowSize; i++)
        {
            x[i].R = input[i];
            x[i].I = 0;
        }
        _fft.Forward(x, _fftOut);
        Array.Copy(_fftOut, output, FreqSize);
    }

    // The inverse runs the forward transform on the conjugate-symmetric spectrum and reads the
    // result backwards, as denoise.c does.
    private void InverseTransform(Span<float> output, KissCpx[] input)
    {
        KissCpx[] x = _fftIn;
        Array.Copy(input, x, FreqSize);
        for (int i = FreqSize; i < WindowSize; i++)
        {
            x[i].R = x[WindowSize - i].R;
            x[i].I = -x[WindowSize - i].I;
        }
        _fft.Forward(x, _fftOut);
        KissCpx[] y = _fftOut;
        output[0] = WindowSize * y[0].R;
        for (int i = 1; i < WindowSize; i++)
            output[i] = WindowSize * y[WindowSize - i].R;
    }

    // Triangular band weighting; the first and last half bands fold into their neighbours.
    private void ComputeBandEnergy(Span<float> bandE, ReadOnlySpan<KissCpx> x)
    {
        Span<float> sum = _bandSum;
        sum.Clear();
        ReadOnlySpan<int> edges = BandEdges;
        for (int i = 0; i < Bands + 1; i++)
        {
            int start = edges[i];
            int size = edges[i + 1] - start;
            for (int j = 0; j < size; j++)
            {
                float frac = (float)j / size;
                KissCpx v = x[start + j];
                float tmp = v.R * v.R;
                tmp += v.I * v.I;
                sum[i] += (1 - frac) * tmp;
                sum[i + 1] += frac * tmp;
            }
        }
        sum[1] = (sum[0] + sum[1]) * 2 / 3;
        sum[Bands] = (sum[Bands] + sum[Bands + 1]) * 2 / 3;
        sum.Slice(1, Bands).CopyTo(bandE);
    }

    private void ComputeBandCorrelation(Span<float> bandE, ReadOnlySpan<KissCpx> x, ReadOnlySpan<KissCpx> p)
    {
        Span<float> sum = _bandSum;
        sum.Clear();
        ReadOnlySpan<int> edges = BandEdges;
        for (int i = 0; i < Bands + 1; i++)
        {
            int start = edges[i];
            int size = edges[i + 1] - start;
            for (int j = 0; j < size; j++)
            {
                float frac = (float)j / size;
                KissCpx a = x[start + j];
                KissCpx b = p[start + j];
                float tmp = a.R * b.R;
                tmp += a.I * b.I;
                sum[i] += (1 - frac) * tmp;
                sum[i + 1] += frac * tmp;
            }
        }
        sum[1] = (sum[0] + sum[1]) * 2 / 3;
        sum[Bands] = (sum[Bands] + sum[Bands + 1]) * 2 / 3;
        sum.Slice(1, Bands).CopyTo(bandE);
    }

    // Linear interpolation of band values onto bins 0..399.
    private static void InterpBandGain(Span<float> g, ReadOnlySpan<float> bandE)
    {
        ReadOnlySpan<int> edges = BandEdges;
        for (int i = 1; i < Bands; i++)
        {
            int start = edges[i];
            int size = edges[i + 1] - start;
            for (int j = 0; j < size; j++)
            {
                float frac = (float)j / size;
                g[start + j] = (1 - frac) * bandE[i - 1] + frac * bandE[i];
            }
        }
        for (int j = 0; j < edges[1]; j++)
            g[j] = bandE[0];
        for (int j = edges[Bands]; j < edges[Bands + 1]; j++)
            g[j] = bandE[Bands - 1];
    }

    private void Dct(Span<float> output, ReadOnlySpan<float> input)
    {
        float[] table = _model.DctTable;
        for (int i = 0; i < Bands; i++)
        {
            float sum = 0;
            for (int j = 0; j < Bands; j++)
                sum += input[j] * table[j * Bands + i];
            output[i] = (float)(sum * DctScale);
        }
    }
}
