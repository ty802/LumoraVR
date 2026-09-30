// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// RMS and peak over a sliding window, both channels together, in linear amplitude: a full-scale sine in both
// channels reads 0.707 RMS and 1 peak. The window moves in slices, so a reading is at most one slice old and
// costs nothing to take. Allocation free once built.
public sealed class LevelMeter
{
    private readonly double[] _sliceSquares;
    private readonly float[] _slicePeaks;
    private readonly int _sliceLength;
    private int _slice;
    private int _inSlice;
    private double _squares;
    private float _peak;

    public LevelMeter(int sampleRate, float windowSeconds = 0.05f, int slices = 5)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        slices = Math.Clamp(slices, 1, 100);
        float seconds = float.IsFinite(windowSeconds) ? Math.Clamp(windowSeconds, 0.001f, 10f) : 0.05f;
        _sliceLength = Math.Max(1, (int)Math.Round(sampleRate * seconds / slices));
        _sliceSquares = new double[slices];
        _slicePeaks = new float[slices];
        WindowSeconds = _sliceLength * slices / (float)sampleRate;
    }

    public float WindowSeconds { get; }

    public float Rms { get; private set; }

    public float Peak { get; private set; }

    public void Reset()
    {
        Array.Clear(_sliceSquares);
        Array.Clear(_slicePeaks);
        _slice = 0;
        _inSlice = 0;
        _squares = 0.0;
        _peak = 0f;
        Rms = 0f;
        Peak = 0f;
    }

    public void Write(ReadOnlySpan<StereoSample> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float left = samples[i].left;
            float right = samples[i].right;
            _squares += (double)left * left + (double)right * right;
            float a = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (a > _peak)
                _peak = a;
            if (++_inSlice < _sliceLength)
                continue;

            _sliceSquares[_slice] = _squares;
            _slicePeaks[_slice] = _peak;
            _slice = (_slice + 1) % _sliceSquares.Length;
            _squares = 0.0;
            _peak = 0f;
            _inSlice = 0;

            double total = 0.0;
            float peak = 0f;
            for (int s = 0; s < _sliceSquares.Length; s++)
            {
                total += _sliceSquares[s];
                peak = MathF.Max(peak, _slicePeaks[s]);
            }
            Rms = (float)Math.Sqrt(total / (2.0 * _sliceLength * _sliceSquares.Length));
            Peak = peak;
        }
    }
}
