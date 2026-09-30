// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Loudness as EBU R128 measures it, in LUFS, stereo.
//
// Both channels go through K-weighting and the two mean squares add (left and right weigh 1 each); loudness
// is -0.691 + 10 log10 of that sum. The signal is kept as mean squares of 100 ms slices: momentary is the
// last four of them (400 ms), short-term the last thirty (3 s), both updated every slice. Every slice also
// closes one 400 ms gating block, 75% overlapped with the one before, and integrated loudness is the mean
// of the blocks louder than -70 LUFS and louder than the mean of those less 10 LU.
//
// The gating blocks go into a histogram of 0.02 LU bins that keeps each bin's exact energy sum, so an hour
// of blocks costs the same memory as a second of them; the only approximation is which side of the
// relative gate the blocks within 0.02 LU of it land on. Silence reads negative infinity. Allocation free
// once built. -xlinka
public sealed class LoudnessMeter
{
    public const float AbsoluteGate = -70f;
    public const float RelativeGate = -10f;

    private const double Offset = -0.691;
    private const double HistogramFloor = -70.0;
    private const double HistogramStep = 0.02;
    private const int HistogramBins = 4000;
    private const int MomentarySlices = 4;
    private const int ShortTermSlices = 30;

    private readonly KWeighting _weighting;
    private readonly int _sliceLength;
    private readonly double[] _slices = new double[ShortTermSlices];
    private readonly long[] _binCounts = new long[HistogramBins];
    private readonly double[] _binEnergy = new double[HistogramBins];
    private KWeighting.State _left;
    private KWeighting.State _right;
    private double _current;
    private int _inSlice;
    private int _slice;
    private long _slicesSeen;
    private long _gatedCount;
    private double _gatedEnergy;

    public LoudnessMeter(int sampleRate)
    {
        _weighting = new KWeighting(sampleRate);
        _sliceLength = Math.Max(1, (int)Math.Round(sampleRate * 0.1));
        Reset();
    }

    public int SampleRate => _weighting.SampleRate;

    public KWeighting Weighting => _weighting;

    public float Momentary { get; private set; }

    public float ShortTerm { get; private set; }

    public float Integrated { get; private set; }

    // Gating blocks measured since the integrated reading was last reset.
    public long Blocks { get; private set; }

    public void Reset()
    {
        _left = default;
        _right = default;
        _current = 0.0;
        _inSlice = 0;
        _slice = 0;
        _slicesSeen = 0;
        Array.Clear(_slices);
        Momentary = float.NegativeInfinity;
        ShortTerm = float.NegativeInfinity;
        ResetIntegrated();
    }

    public void ResetIntegrated()
    {
        Array.Clear(_binCounts);
        Array.Clear(_binEnergy);
        _gatedCount = 0;
        _gatedEnergy = 0.0;
        Blocks = 0;
        Integrated = float.NegativeInfinity;
    }

    public void Write(ReadOnlySpan<StereoSample> samples)
    {
        var weighting = _weighting;
        for (int i = 0; i < samples.Length; i++)
        {
            double l = weighting.Process(samples[i].left, ref _left);
            double r = weighting.Process(samples[i].right, ref _right);
            _current += l * l + r * r;
            if (++_inSlice == _sliceLength)
                CloseSlice();
        }
        _left.Flush();
        _right.Flush();
    }

    private void CloseSlice()
    {
        _slices[_slice] = _current / _sliceLength;
        _slice = (_slice + 1) % ShortTermSlices;
        _current = 0.0;
        _inSlice = 0;
        _slicesSeen++;

        double momentary = 0.0;
        double shortTerm = 0.0;
        for (int back = 1; back <= ShortTermSlices; back++)
        {
            double energy = _slices[(_slice - back + ShortTermSlices) % ShortTermSlices];
            if (back <= MomentarySlices)
                momentary += energy;
            shortTerm += energy;
        }
        momentary /= MomentarySlices;
        shortTerm /= ShortTermSlices;
        Momentary = ToLufs(momentary);
        ShortTerm = ToLufs(shortTerm);

        if (_slicesSeen >= MomentarySlices)
            AddBlock(momentary);
    }

    private void AddBlock(double energy)
    {
        Blocks++;
        if (energy <= 0.0)
            return;
        double loudness = Offset + 10.0 * Math.Log10(energy);
        if (!(loudness > AbsoluteGate))
            return;
        int bin = Math.Clamp((int)((loudness - HistogramFloor) / HistogramStep), 0, HistogramBins - 1);
        _binCounts[bin]++;
        _binEnergy[bin] += energy;
        _gatedCount++;
        _gatedEnergy += energy;

        double gate = Offset + 10.0 * Math.Log10(_gatedEnergy / _gatedCount) + RelativeGate;
        int from = Math.Clamp((int)Math.Round((gate - HistogramFloor) / HistogramStep), 0, HistogramBins);
        long count = 0;
        double sum = 0.0;
        for (int b = from; b < HistogramBins; b++)
        {
            count += _binCounts[b];
            sum += _binEnergy[b];
        }
        Integrated = count > 0 ? ToLufs(sum / count) : float.NegativeInfinity;
    }

    private static float ToLufs(double energy) => energy > 0.0 ? (float)(Offset + 10.0 * Math.Log10(energy)) : float.NegativeInfinity;
}
