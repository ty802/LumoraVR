// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Everything the analyser reads off one stereo stream, fed as it arrives.
//
// Loudness and the level meter take both channels; the spectrum, onsets, tempo and pitch take the mean of the two.
// The spectrum runs at the configured FFT size with a hop of a quarter frame, never slower than sixty frames
// a second, and gathers into bands whose levels are amplitudes: a sine of amplitude A inside a band reads A,
// the band's power is (level^2) / 2. Onsets, tempo and pitch each keep a fixed framing, so they read the
// same whatever spectrum was asked for.
//
// For a machine's world thread, not the audio thread: a few hundred microseconds a second of audio at the
// defaults, and nothing allocated once built. -xlinka
public sealed class AudioAnalysis
{
    private const int Chunk = 1024;

    private readonly SpectrumAnalyzer _spectrum;
    private readonly FrequencyBands _bands;
    private readonly LevelMeter _level;
    private readonly LoudnessMeter _loudness;
    private readonly OnsetDetector _onsets;
    private readonly TempoEstimator _tempo;
    private readonly PitchDetector _pitch;
    private readonly float[] _mono = new float[Chunk];
    private readonly float[] _bandPowers;
    private readonly float[] _bandLevels;
    private readonly float _keep;
    private bool _banded;

    public AudioAnalysis(int sampleRate, in AudioAnalysisSettings settings)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;
        Settings = settings.Clamped();
        int size = Settings.FftSize;
        int hop = Math.Max(1, Math.Min(size / 4, sampleRate / 60));
        _spectrum = new SpectrumAnalyzer(size, hop, sampleRate, Settings.Window);
        _bands = new FrequencyBands(Settings.BandScale, Settings.BandCount, Settings.MinFrequency, Settings.MaxFrequency, size, sampleRate);
        _bandPowers = new float[_bands.Count];
        _bandLevels = new float[_bands.Count];
        _keep = MathF.Pow(Settings.Smoothing, hop / (sampleRate / 60f));
        _level = new LevelMeter(sampleRate);
        _loudness = new LoudnessMeter(sampleRate);
        _tempo = new TempoEstimator(sampleRate / (float)Math.Max(1, (int)Math.Round(sampleRate / 100.0)));
        _onsets = new OnsetDetector(sampleRate, _tempo);
        _pitch = new PitchDetector(sampleRate);
    }

    public int SampleRate { get; }

    public AudioAnalysisSettings Settings { get; }

    public FrequencyBands Bands => _bands;

    public int BandCount => _bandLevels.Length;

    public ReadOnlySpan<float> BandLevels => _bandLevels;

    public SpectrumAnalyzer Spectrum => _spectrum;

    public long SamplesAnalyzed { get; private set; }

    public float Rms => _level.Rms;

    public float Peak => _level.Peak;

    public float Momentary => _loudness.Momentary;

    public float ShortTerm => _loudness.ShortTerm;

    public float Integrated => _loudness.Integrated;

    public float Centroid { get; private set; }

    public float Flatness { get; private set; }

    public float Rolloff { get; private set; }

    public float Tempo => _tempo.Bpm;

    public float TempoConfidence => _tempo.Confidence;

    public float Pitch => _pitch.Frequency;

    public float PitchConfidence => _pitch.Confidence;

    public long Onsets => _onsets.Onsets;

    public float LastOnsetStrength => _onsets.LastStrength;

    public double LastOnsetSeconds => _onsets.LastOnsetSeconds;

    public float BandLevel(int band) => (uint)band < (uint)_bandLevels.Length ? _bandLevels[band] : 0f;

    public void Reset()
    {
        _spectrum.Reset();
        _level.Reset();
        _loudness.Reset();
        _onsets.Reset();
        _pitch.Reset();
        Array.Clear(_bandLevels);
        _banded = false;
        Centroid = 0f;
        Flatness = 0f;
        Rolloff = 0f;
        SamplesAnalyzed = 0;
    }

    public void ResetIntegrated() => _loudness.ResetIntegrated();

    public void Write(ReadOnlySpan<StereoSample> samples)
    {
        _level.Write(samples);
        _loudness.Write(samples);
        while (samples.Length > 0)
        {
            int n = Math.Min(Chunk, samples.Length);
            for (int i = 0; i < n; i++)
                _mono[i] = 0.5f * (samples[i].left + samples[i].right);
            ReadOnlySpan<float> mono = _mono.AsSpan(0, n);
            _onsets.Write(mono);
            _pitch.Write(mono);
            while (mono.Length > 0)
            {
                int taken = _spectrum.Write(mono);
                mono = mono.Slice(taken);
                if (_spectrum.TryAnalyze())
                    Frame();
            }
            samples = samples.Slice(n);
            SamplesAnalyzed += n;
        }
    }

    private void Frame()
    {
        _bands.Measure(_spectrum.Powers, _bandPowers);
        float keep = _banded ? _keep : 0f;
        for (int b = 0; b < _bandLevels.Length; b++)
        {
            float level = MathF.Sqrt(2f * MathF.Max(0f, _bandPowers[b]));
            _bandLevels[b] = keep * _bandLevels[b] + (1f - keep) * level;
        }
        _banded = true;
        Centroid = SpectralShape.Centroid(_spectrum.Magnitudes, _spectrum.BinWidth);
        Flatness = SpectralShape.Flatness(_spectrum.Powers);
        Rolloff = SpectralShape.Rolloff(_spectrum.Powers, _spectrum.BinWidth);
    }
}
