// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Beats per minute from an onset strength envelope, one value per analysis frame.
//
// The last few seconds of the envelope, mean removed and lightly smoothed, are autocorrelated over the lags
// a beat can take. A beat rarely lasts a whole number of frames, so candidate periods are tried in quarter
// frames with the correlation interpolated between lags; scoring only whole lags lets a period of 37.5
// frames split its peak across 37 and 38 and lose to its own double, which lands on 75 exactly. A period's
// score is its correlation plus half the correlation at twice the period, which favours the true beat over
// its double (a steady pulse correlates at every multiple, and the half-tempo candidate never collects the
// extra term the right one does), weighted by a log-normal preference one octave wide around 120 BPM for
// what the correlation cannot decide. The best candidate is refined by a parabola through its neighbours.
// Confidence is the correlation at that period over the envelope's energy: a steady pulse reads near 1,
// music with a clear beat around 0.3 to 0.7, noise near 0.
//
// Re-estimated a few times a second rather than every frame; allocation free once built. Tempos above
// about 170 BPM read as half of themselves, which is where a listener starts to count them that way too.
public sealed class TempoEstimator
{
    private const double CenterBpm = 120.0;
    private const double SpreadOctaves = 1.0;
    private const double MinHistorySeconds = 3.0;
    private const double Step = 0.25;

    private readonly float[] _history;
    private readonly double[] _envelope;
    private readonly double[] _correlation;
    private readonly int _minLag;
    private readonly int _maxLag;
    private readonly int _updateFrames;
    private readonly int _minFrames;
    private int _write;
    private long _pushed;
    private int _sinceUpdate;

    public TempoEstimator(float frameRate, float minBpm = 50f, float maxBpm = 220f, float historySeconds = 8f, float updateSeconds = 0.25f)
    {
        if (!(frameRate > 0f))
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        FrameRate = frameRate;
        MinBpm = Math.Clamp(minBpm, 20f, 400f);
        MaxBpm = Math.Clamp(maxBpm, MinBpm + 1f, 600f);
        int length = Math.Max(16, (int)Math.Round(frameRate * Math.Clamp(historySeconds, 2f, 60f)));
        _history = new float[length];
        _envelope = new double[length];
        _minLag = Math.Max(1, (int)Math.Floor(60.0 * frameRate / MaxBpm));
        _maxLag = Math.Min(length / 3, (int)Math.Ceiling(60.0 * frameRate / MinBpm));
        _correlation = new double[2 * _maxLag + 3];
        _updateFrames = Math.Max(1, (int)Math.Round(frameRate * updateSeconds));
        _minFrames = Math.Min(length, (int)Math.Round(frameRate * MinHistorySeconds));
    }

    public float FrameRate { get; }

    public float MinBpm { get; }

    public float MaxBpm { get; }

    // 0 until there is enough history to say.
    public float Bpm { get; private set; }

    public float Confidence { get; private set; }

    public void Reset()
    {
        Array.Clear(_history);
        _write = 0;
        _pushed = 0;
        _sinceUpdate = 0;
        Bpm = 0f;
        Confidence = 0f;
    }

    public void Push(float strength)
    {
        _history[_write] = float.IsFinite(strength) ? strength : 0f;
        _write = (_write + 1) % _history.Length;
        _pushed++;
        if (++_sinceUpdate >= _updateFrames && _pushed >= _minFrames)
        {
            _sinceUpdate = 0;
            Estimate();
        }
    }

    private void Estimate()
    {
        int n = (int)Math.Min(_pushed, _history.Length);
        int start = (_write - n + _history.Length) % _history.Length;
        double mean = 0.0;
        for (int i = 0; i < n; i++)
            mean += _history[(start + i) % _history.Length];
        mean /= n;
        double previous = _history[start] - mean;
        for (int i = 0; i < n; i++)
        {
            double current = _history[(start + i) % _history.Length] - mean;
            double next = i + 1 < n ? _history[(start + i + 1) % _history.Length] - mean : current;
            _envelope[i] = 0.25 * previous + 0.5 * current + 0.25 * next;
            previous = current;
        }
        double energy = 0.0;
        for (int i = 0; i < n; i++)
            energy += _envelope[i] * _envelope[i];
        if (energy <= 1e-12)
        {
            Bpm = 0f;
            Confidence = 0f;
            return;
        }

        int top = Math.Min(_correlation.Length - 1, n - 1);
        for (int lag = 0; lag <= top; lag++)
        {
            double sum = 0.0;
            for (int i = lag; i < n; i++)
                sum += _envelope[i] * _envelope[i - lag];
            _correlation[lag] = sum;
        }

        double best = -1.0;
        double bestScore = double.NegativeInfinity;
        double longest = Math.Min(_maxLag, top - 1);
        for (double period = _minLag; period <= longest; period += Step)
        {
            double score = Score(period, top);
            if (score > bestScore)
            {
                bestScore = score;
                best = period;
            }
        }
        if (best < 0.0 || bestScore <= 0.0)
        {
            Confidence = 0f;
            return;
        }

        double refined = best;
        if (best - Step >= 1.0 && best + Step <= top)
        {
            double before = Score(best - Step, top);
            double after = Score(best + Step, top);
            double curve = before - 2.0 * bestScore + after;
            if (curve < 0.0)
                refined += Step * Math.Clamp(0.5 * (before - after) / curve, -0.5, 0.5);
        }
        Bpm = (float)(60.0 * FrameRate / refined);
        Confidence = (float)Math.Clamp(At(refined, top) / energy, 0.0, 1.0);
    }

    // Correlation at a fractional lag, straight line between the lags either side.
    private double At(double lag, int top)
    {
        if (lag < 0.0 || lag > top)
            return 0.0;
        int whole = (int)lag;
        double part = lag - whole;
        return whole + 1 <= top ? _correlation[whole] + part * (_correlation[whole + 1] - _correlation[whole]) : _correlation[whole];
    }

    private double Score(double period, int top)
    {
        double value = At(period, top);
        if (2.0 * period <= top)
            value += 0.5 * At(2.0 * period, top);
        double octaves = Math.Log2(60.0 * FrameRate / period / CenterBpm) / SpreadOctaves;
        return value * Math.Exp(-0.5 * octaves * octaves);
    }
}
