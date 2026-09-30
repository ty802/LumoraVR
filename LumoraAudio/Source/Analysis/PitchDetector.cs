// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Fundamental frequency by YIN (de Cheveigne and Kawahara, 2002).
//
// The difference function d(t) = sum over the window of (x[j] - x[j + t])^2 is how unlike itself the signal
// is one lag later. Divided by its own running mean over the smaller lags it becomes d', which starts at 1
// and dips toward 0 wherever the signal repeats. The period is the smallest lag where d' drops under the
// threshold, followed down to the bottom of that dip, and refined by a parabola through the dip and its two
// neighbours. Taking the earliest dip rather than the deepest is what keeps a voice from being heard an octave
// low: every multiple of the period dips too. Confidence is 1 - d' at the chosen lag, so a clean tone reads
// near 1, a voice around 0.8 to 0.95, and noise, which never repeats, low; when nothing dips under the
// threshold there is no pitch and the frequency reads 0.
//
// d(t) = e(0) + e(t) - 2 r(t): the two energies come from running sums of squares and the cross term r from
// one real FFT correlation, so a lag range of a thousand costs three transforms instead of a million
// multiplies. -xlinka
public sealed class PitchDetector
{
    public const float DefaultThreshold = 0.15f;

    // Below this RMS a frame is silence and has no pitch.
    private const double SilenceRms = 1e-4;

    private readonly int _window;
    private readonly int _minLag;
    private readonly int _maxLag;
    private readonly float[] _buffer;
    private readonly RealFft _fft;
    private readonly float[] _a;
    private readonly float[] _b;
    private readonly float[] _ar;
    private readonly float[] _ai;
    private readonly float[] _br;
    private readonly float[] _bi;
    private readonly float[] _cross;
    private readonly float[] _scratch;
    private readonly double[] _prefix;
    private readonly double[] _normalized;
    private readonly int _hop;
    private int _filled;
    private int _sinceAnalysis;

    public PitchDetector(int sampleRate, float minHz = 50f, float maxHz = 1500f, float threshold = DefaultThreshold, int hop = 0)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;
        MinHz = Math.Clamp(float.IsFinite(minHz) ? minHz : 50f, 20f, sampleRate / 8f);
        MaxHz = Math.Clamp(float.IsFinite(maxHz) ? maxHz : 1500f, MinHz * 2f, sampleRate / 4f);
        Threshold = Math.Clamp(float.IsFinite(threshold) ? threshold : DefaultThreshold, 0.01f, 0.9f);
        _maxLag = (int)Math.Ceiling(sampleRate / MinHz) + 1;
        _minLag = Math.Max(2, (int)Math.Floor(sampleRate / MaxHz));
        _window = _maxLag;
        int length = _window + _maxLag + 1;
        _buffer = new float[length];
        _fft = RealFft.Get(FftSizes.NextFast(length));
        int bins = _fft.BinCount;
        _a = new float[_fft.Size];
        _b = new float[_fft.Size];
        _ar = new float[bins];
        _ai = new float[bins];
        _br = new float[bins];
        _bi = new float[bins];
        _cross = new float[_fft.Size];
        _scratch = new float[_fft.ScratchLength];
        _prefix = new double[length + 1];
        _normalized = new double[_maxLag + 2];
        _hop = hop > 0 ? hop : Math.Max(1, (int)Math.Round(sampleRate / 100.0));
    }

    public int SampleRate { get; }

    public float MinHz { get; }

    public float MaxHz { get; }

    public float Threshold { get; }

    // Samples a detection looks at: the integration window plus the longest lag.
    public int Span => _buffer.Length;

    // Hz, 0 when the last frame had no pitch.
    public float Frequency { get; private set; }

    public float Confidence { get; private set; }

    public bool IsVoiced => Frequency > 0f;

    public void Reset()
    {
        Array.Clear(_buffer);
        _filled = 0;
        _sinceAnalysis = 0;
        Frequency = 0f;
        Confidence = 0f;
    }

    // Streams samples in; a detection runs every hop once a full span has arrived.
    public void Write(ReadOnlySpan<float> samples)
    {
        int length = _buffer.Length;
        while (samples.Length > 0)
        {
            int free = length - _filled;
            if (free == 0)
            {
                int shift = Math.Min(samples.Length, _hop - _sinceAnalysis);
                Array.Copy(_buffer, shift, _buffer, 0, length - shift);
                samples.Slice(0, shift).CopyTo(_buffer.AsSpan(length - shift));
                samples = samples.Slice(shift);
                _sinceAnalysis += shift;
            }
            else
            {
                int take = Math.Min(free, samples.Length);
                samples.Slice(0, take).CopyTo(_buffer.AsSpan(_filled));
                _filled += take;
                samples = samples.Slice(take);
                _sinceAnalysis += take;
            }
            if (_filled == length && _sinceAnalysis >= _hop)
            {
                _sinceAnalysis = 0;
                Frequency = Detect(_buffer, out float confidence);
                Confidence = confidence;
            }
        }
    }

    // One detection over frame[0..Span).
    public float Detect(ReadOnlySpan<float> frame, out float confidence)
    {
        confidence = 0f;
        int length = _buffer.Length;
        if (frame.Length < length)
            throw new ArgumentException($"a detection looks at {length} samples");
        int w = _window;

        double running = 0.0;
        _prefix[0] = 0.0;
        for (int i = 0; i < length; i++)
        {
            running += (double)frame[i] * frame[i];
            _prefix[i + 1] = running;
        }
        double e0 = _prefix[w];
        if (e0 < SilenceRms * SilenceRms * w)
            return 0f;

        Array.Clear(_a);
        Array.Clear(_b);
        frame.Slice(0, w).CopyTo(_a);
        frame.Slice(0, length).CopyTo(_b);
        _fft.Forward(_a, _ar, _ai, _scratch);
        _fft.Forward(_b, _br, _bi, _scratch);
        for (int k = 0; k < _ar.Length; k++)
        {
            // conj(A) B
            float re = _ar[k] * _br[k] + _ai[k] * _bi[k];
            float im = _ar[k] * _bi[k] - _ai[k] * _br[k];
            _ar[k] = re;
            _ai[k] = im;
        }
        _fft.Inverse(_ar, _ai, _cross, _scratch);

        _normalized[0] = 1.0;
        double cumulative = 0.0;
        for (int t = 1; t <= _maxLag; t++)
        {
            double et = _prefix[t + w] - _prefix[t];
            double d = Math.Max(0.0, e0 + et - 2.0 * _cross[t]);
            cumulative += d;
            _normalized[t] = cumulative > 0.0 ? d * t / cumulative : 1.0;
        }

        int best = -1;
        for (int t = _minLag; t < _maxLag; t++)
        {
            if (_normalized[t] < Threshold)
            {
                while (t + 1 < _maxLag && _normalized[t + 1] < _normalized[t])
                    t++;
                best = t;
                break;
            }
        }
        if (best < 0)
        {
            double lowest = double.MaxValue;
            for (int t = _minLag; t < _maxLag; t++)
            {
                if (_normalized[t] < lowest)
                {
                    lowest = _normalized[t];
                    best = t;
                }
            }
            confidence = (float)Math.Clamp(1.0 - lowest, 0.0, 1.0);
            return 0f;
        }

        double refined = best;
        double before = _normalized[best - 1];
        double at = _normalized[best];
        double after = _normalized[best + 1];
        double curve = before - 2.0 * at + after;
        if (curve > 0.0)
            refined += Math.Clamp(0.5 * (before - after) / curve, -0.5, 0.5);
        confidence = (float)Math.Clamp(1.0 - at, 0.0, 1.0);
        return (float)(SampleRate / refined);
    }

    // MIDI note number of a frequency, A4 = 440 Hz = 69, fractional.
    public static float NoteOf(float hz, float tuning = 440f)
        => hz > 0f && tuning > 0f ? 69f + 12f * MathF.Log2(hz / tuning) : float.NaN;

    public static float FrequencyOf(float note, float tuning = 440f) => tuning * MathF.Pow(2f, (note - 69f) / 12f);
}
