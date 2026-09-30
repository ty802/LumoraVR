// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Where sounds start: a drum hit, a plucked note, a clap.
//
// Every 10 ms a short frame's power is gathered into mel bands and put in decibels, floored well below
// anything audible so silence reads as a flat line. The onset strength is spectral flux: how many decibels
// the bands rose since the last frame, averaged, with falls counted as nothing. A frame is an onset when its
// flux is the highest within 30 ms before and 10 ms after it, clears an adaptive threshold (1.5 times the
// median flux of the last half second plus 1.5 dB, so a busy passage has to rise further than a quiet one),
// and comes at least 50 ms after the last onset. The 10 ms look-ahead is the detector's whole latency on
// top of its frame.
//
// The flux of every frame goes to a TempoEstimator, if one is given. Allocation free once built. -xlinka
public sealed class OnsetDetector
{
    private const int MelBands = 40;
    private const float LowHz = 30f;
    private const float HighHz = 16000f;
    private const double FloorPower = 1e-9;
    private const float MedianSeconds = 0.5f;
    private const float Lambda = 1.5f;
    private const float DeltaDb = 1.5f;
    private const float GapSeconds = 0.05f;
    private const int PreFrames = 3;
    private const int PostFrames = 1;

    private readonly SpectrumAnalyzer _spectrum;
    private readonly FrequencyBands _bands;
    private readonly float[] _bandPowers;
    private readonly float[] _level;
    private readonly float[] _previous;
    private readonly float[] _flux;
    private readonly float[] _median;
    private readonly int _medianFrames;
    private readonly int _gapFrames;
    private readonly TempoEstimator? _tempo;
    private long _frame;
    private long _lastOnset = long.MinValue / 2;
    private bool _primed;

    public OnsetDetector(int sampleRate, TempoEstimator? tempo = null)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;
        int hop = Math.Max(1, (int)Math.Round(sampleRate / 100.0));
        int size = FftSizes.NextFast(Math.Max(64, (int)Math.Round(sampleRate * 0.0213)));
        _spectrum = new SpectrumAnalyzer(size, hop, sampleRate, WindowShape.Hann);
        _bands = new FrequencyBands(FrequencyScale.Mel, MelBands, LowHz, MathF.Min(HighHz, sampleRate * 0.45f), size, sampleRate);
        _bandPowers = new float[_bands.Count];
        _level = new float[_bands.Count];
        _previous = new float[_bands.Count];
        _medianFrames = Math.Max(3, (int)Math.Round(MedianSeconds * 100.0));
        _flux = new float[_medianFrames + PostFrames + PreFrames + 2];
        _median = new float[_medianFrames + PostFrames + 1];
        _gapFrames = Math.Max(1, (int)Math.Round(GapSeconds * 100.0));
        _tempo = tempo;
    }

    public int SampleRate { get; }

    public float FrameRate => SampleRate / (float)_spectrum.Hop;

    public int FrameSize => _spectrum.FftSize;

    public int Hop => _spectrum.Hop;

    public TempoEstimator? Tempo => _tempo;

    public long Onsets { get; private set; }

    // Stream time of the latest onset in seconds, counted from the start of the stream, and how far its flux
    // cleared the threshold, in dB.
    public double LastOnsetSeconds { get; private set; } = double.NegativeInfinity;

    public float LastStrength { get; private set; }

    // The latest frame's flux, in dB.
    public float Flux { get; private set; }

    public void Reset()
    {
        _spectrum.Reset();
        Array.Clear(_previous);
        Array.Clear(_flux);
        _frame = 0;
        _lastOnset = long.MinValue / 2;
        _primed = false;
        Onsets = 0;
        LastOnsetSeconds = double.NegativeInfinity;
        LastStrength = 0f;
        Flux = 0f;
        _tempo?.Reset();
    }

    // Returns how many onsets this write found.
    public int Write(ReadOnlySpan<float> samples)
    {
        int found = 0;
        while (samples.Length > 0)
        {
            int taken = _spectrum.Write(samples);
            samples = samples.Slice(taken);
            if (_spectrum.TryAnalyze() && Frame())
                found++;
        }
        return found;
    }

    private bool Frame()
    {
        _bands.Measure(_spectrum.Powers, _bandPowers);
        double rise = 0.0;
        for (int b = 0; b < _level.Length; b++)
        {
            float level = (float)(10.0 * Math.Log10(Math.Max(_bandPowers[b], FloorPower)));
            _level[b] = level;
            float step = level - _previous[b];
            if (step > 0f)
                rise += step;
            _previous[b] = level;
        }
        float flux = _primed ? (float)(rise / _level.Length) : 0f;
        _primed = true;
        Flux = flux;
        _tempo?.Push(flux);

        long frame = _frame++;
        _flux[(int)(frame % _flux.Length)] = flux;
        return Pick(frame);
    }

    // Judges the frame PostFrames back, once the frames after it are in.
    private bool Pick(long newest)
    {
        long candidate = newest - PostFrames;
        if (candidate < PreFrames)
            return false;
        float value = At(candidate);
        for (long f = candidate - PreFrames; f <= newest; f++)
        {
            if (f == candidate)
                continue;
            float other = At(f);
            if (other > value || (f < candidate && other == value))
                return false;
        }
        if (candidate - _lastOnset < _gapFrames)
            return false;

        long first = Math.Max(0, newest - _median.Length + 1);
        int count = 0;
        for (long f = first; f <= newest; f++)
            _median[count++] = At(f);
        Span<float> window = _median.AsSpan(0, count);
        window.Sort();
        float median = count % 2 == 1 ? window[count / 2] : 0.5f * (window[count / 2 - 1] + window[count / 2]);
        float threshold = Lambda * median + DeltaDb;
        if (!(value > threshold))
            return false;

        _lastOnset = candidate;
        Onsets++;
        LastStrength = value - threshold;
        // The flux jumps when the new sound reaches the newest hop of the frame; that hop's middle is the time.
        LastOnsetSeconds = (_spectrum.FftSize + candidate * _spectrum.Hop - 0.5 * _spectrum.Hop) / SampleRate;
        return true;
    }

    private float At(long frame) => _flux[(int)(frame % _flux.Length)];
}
