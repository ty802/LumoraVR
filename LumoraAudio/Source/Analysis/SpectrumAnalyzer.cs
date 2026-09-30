// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A running spectrum of one channel, a frame every Hop samples.
//
// Two calibrations, because a spectrum answers two different questions. Magnitudes are scaled by the
// window's own sum, so a sine centred on a bin reads its amplitude there whatever the window. Powers are
// scaled by the window's energy, so they sum over the bins to the frame's mean square and a band of them
// is a share of the signal's power; that is what band levels and loudness-like readings are built on.
// Write takes samples until a frame is ready, TryAnalyze turns it into the spectrum:
//
//   while (samples.Length > 0) { samples = samples[analyzer.Write(samples)..]; if (analyzer.TryAnalyze()) use it; }
//
// Allocation free once built. -xlinka
public sealed class SpectrumAnalyzer
{
    private readonly StftAnalyzer _stft;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly float[] _magnitudes;
    private readonly float[] _powers;
    private readonly float _magnitudeScale;
    private readonly float _powerScale;

    public SpectrumAnalyzer(int fftSize, int hop, int sampleRate, WindowShape window = WindowShape.Hann, float kaiserBeta = SpectralWindow.DefaultKaiserBeta)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _stft = new StftAnalyzer(fftSize, hop, window, kaiserBeta, primed: false);
        FftSize = fftSize;
        Hop = hop;
        SampleRate = sampleRate;
        Window = window;
        int bins = _stft.BinCount;
        _re = new float[bins];
        _im = new float[bins];
        _magnitudes = new float[bins];
        _powers = new float[bins];
        double sum = 0.0;
        double squares = 0.0;
        foreach (float w in _stft.Window)
        {
            sum += w;
            squares += (double)w * w;
        }
        _magnitudeScale = (float)(1.0 / Math.Max(sum, 1e-30));
        _powerScale = (float)(1.0 / Math.Max(fftSize * squares, 1e-30));
    }

    public int FftSize { get; }

    public int Hop { get; }

    public int SampleRate { get; }

    public WindowShape Window { get; }

    public int BinCount => _magnitudes.Length;

    public float BinWidth => (float)SampleRate / FftSize;

    public long Frames { get; private set; }

    public ReadOnlySpan<float> Magnitudes => _magnitudes;

    public ReadOnlySpan<float> Powers => _powers;

    public float BinFrequency(int bin) => bin * (float)SampleRate / FftSize;

    public void Reset()
    {
        _stft.Reset();
        Array.Clear(_magnitudes);
        Array.Clear(_powers);
        Frames = 0;
    }

    // Takes samples up to the next frame boundary and returns how many it took.
    public int Write(ReadOnlySpan<float> samples) => _stft.Write(samples);

    public bool TryAnalyze()
    {
        if (!_stft.TryRead(_re, _im))
            return false;
        int bins = _magnitudes.Length;
        int nyquist = FftSize % 2 == 0 ? bins - 1 : -1;
        for (int k = 0; k < bins; k++)
        {
            float squared = _re[k] * _re[k] + _im[k] * _im[k];
            // One-sided: every bin but DC and Nyquist stands for itself and its mirror.
            float fold = k == 0 || k == nyquist ? 1f : 2f;
            _magnitudes[k] = MathF.Sqrt(squared) * fold * _magnitudeScale;
            _powers[k] = squared * fold * _powerScale;
        }
        Frames++;
        return true;
    }

    // Magnitudes in decibels relative to a full-scale sine, floored.
    public void CopyDecibels(Span<float> destination, float floor = -120f)
    {
        int n = Math.Min(destination.Length, _magnitudes.Length);
        for (int k = 0; k < n; k++)
        {
            float m = _magnitudes[k];
            destination[k] = m > 0f ? MathF.Max(floor, 20f * MathF.Log10(m)) : floor;
        }
    }
}
