// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Audio;

// A spectrum's bins gathered into bands.
//
// Each bin stands for the stretch of frequency half a bin either side of its centre, and a band takes the
// share of that stretch it covers, so a band narrower than a bin still gets its fraction of the one it sits
// in instead of nothing. Rectangular bands tile the range edge to edge and the triangles of the mel scale
// sum to one from the lowest peak to the highest, so on bin powers the band powers add up to the power in
// the range: no energy counted twice or lost at an edge. -xlinka
public sealed class FrequencyBands
{
    // Base-ten octave ratio of the standard band series.
    private static readonly double OctaveRatio = Math.Pow(10.0, 0.3);

    private readonly float[] _low;
    private readonly float[] _center;
    private readonly float[] _high;
    private readonly int[] _firstBin;
    private readonly int[] _weightStart;
    private readonly int[] _count;
    private readonly float[] _weights;

    public FrequencyBands(FrequencyScale scale, int bandCount, float minHz, float maxHz, int fftSize, int sampleRate)
    {
        if (fftSize < 2)
            throw new ArgumentOutOfRangeException(nameof(fftSize));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        Scale = scale;
        FftSize = fftSize;
        SampleRate = sampleRate;
        double nyquist = sampleRate * 0.5;
        double lo = float.IsFinite(minHz) ? Math.Clamp(minHz, 1.0, nyquist * 0.99) : 20.0;
        double hi = float.IsFinite(maxHz) ? Math.Clamp(maxHz, lo * 1.01, nyquist) : nyquist;
        if (hi <= lo)
            hi = Math.Min(nyquist, lo * 2.0);
        int count = Math.Clamp(bandCount, 1, 1024);

        var edges = new List<(double Low, double Center, double High)>();
        switch (scale)
        {
            case FrequencyScale.Linear:
                for (int b = 0; b < count; b++)
                {
                    double l = lo + (hi - lo) * b / count;
                    double h = lo + (hi - lo) * (b + 1) / count;
                    edges.Add((l, 0.5 * (l + h), h));
                }
                break;
            case FrequencyScale.Logarithmic:
                for (int b = 0; b < count; b++)
                {
                    double l = lo * Math.Pow(hi / lo, (double)b / count);
                    double h = lo * Math.Pow(hi / lo, (double)(b + 1) / count);
                    edges.Add((l, Math.Sqrt(l * h), h));
                }
                break;
            case FrequencyScale.Mel:
                {
                    double melLo = HzToMel(lo);
                    double melHi = HzToMel(hi);
                    for (int b = 0; b < count; b++)
                    {
                        double l = MelToHz(melLo + (melHi - melLo) * b / (count + 1));
                        double c = MelToHz(melLo + (melHi - melLo) * (b + 1) / (count + 1));
                        double h = MelToHz(melLo + (melHi - melLo) * (b + 2) / (count + 1));
                        edges.Add((l, c, h));
                    }
                }
                break;
            default:
                {
                    int per = scale == FrequencyScale.Octave ? 1 : 3;
                    double halfBand = Math.Pow(OctaveRatio, 0.5 / per);
                    int first = (int)Math.Floor(Math.Log(lo / 1000.0, OctaveRatio) * per) - 1;
                    for (int x = first; ; x++)
                    {
                        double center = 1000.0 * Math.Pow(OctaveRatio, (double)x / per);
                        if (center > hi || center * halfBand > nyquist)
                            break;
                        if (center >= lo)
                            edges.Add((center / halfBand, center, center * halfBand));
                    }
                    if (edges.Count == 0)
                        edges.Add((lo, Math.Sqrt(lo * hi), hi));
                }
                break;
        }

        int bands = edges.Count;
        _low = new float[bands];
        _center = new float[bands];
        _high = new float[bands];
        _firstBin = new int[bands];
        _weightStart = new int[bands];
        _count = new int[bands];
        var weights = new List<float>();
        double width = (double)sampleRate / fftSize;
        int bins = fftSize / 2 + 1;
        bool triangles = scale == FrequencyScale.Mel;
        for (int b = 0; b < bands; b++)
        {
            var (l, c, h) = edges[b];
            _low[b] = (float)l;
            _center[b] = (float)c;
            _high[b] = (float)h;
            int start = Math.Max(0, (int)Math.Floor(l / width - 0.5));
            int end = Math.Min(bins - 1, (int)Math.Ceiling(h / width + 0.5));
            int firstUsed = -1;
            int lastUsed = -1;
            var local = new List<float>();
            for (int k = start; k <= end; k++)
            {
                double u = (k - 0.5) * width;
                double v = (k + 0.5) * width;
                double covered = triangles ? TriangleIntegral(l, c, h, u, v) : Math.Max(0.0, Math.Min(v, h) - Math.Max(u, l));
                float weight = (float)(covered / width);
                if (weight <= 0f)
                {
                    if (firstUsed >= 0)
                        local.Add(0f);
                    continue;
                }
                if (firstUsed < 0)
                    firstUsed = k;
                local.Add(weight);
                lastUsed = k;
            }
            if (firstUsed < 0)
                continue;
            int used = lastUsed - firstUsed + 1;
            _firstBin[b] = firstUsed;
            _weightStart[b] = weights.Count;
            _count[b] = used;
            for (int i = 0; i < used; i++)
                weights.Add(local[i]);
        }
        _weights = weights.ToArray();
    }

    public FrequencyScale Scale { get; }

    public int FftSize { get; }

    public int SampleRate { get; }

    public int Count => _low.Length;

    public float Low(int band) => _low[band];

    public float Center(int band) => _center[band];

    public float High(int band) => _high[band];

    // Band powers from bin powers, one per band.
    public void Measure(ReadOnlySpan<float> binPowers, Span<float> bandPowers)
    {
        int bands = Count;
        for (int b = 0; b < bands; b++)
        {
            int count = _count[b];
            if (count == 0)
            {
                bandPowers[b] = 0f;
                continue;
            }
            int bin = _firstBin[b];
            int at = _weightStart[b];
            double sum = 0.0;
            for (int i = 0; i < count; i++)
            {
                int k = bin + i;
                if (k < binPowers.Length)
                    sum += _weights[at + i] * binPowers[k];
            }
            bandPowers[b] = (float)sum;
        }
    }

    // The mel scale as the HTK tools define it.
    public static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);

    public static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

    // The integral over [u, v] of the triangle rising from a to 1 at b and falling back to 0 at c.
    private static double TriangleIntegral(double a, double b, double c, double u, double v)
    {
        double total = 0.0;
        double p = Math.Max(u, a);
        double q = Math.Min(v, b);
        if (q > p && b > a)
            total += ((q - a) * (q - a) - (p - a) * (p - a)) / (2.0 * (b - a));
        p = Math.Max(u, b);
        q = Math.Min(v, c);
        if (q > p && c > b)
            total += ((c - p) * (c - p) - (c - q) * (c - q)) / (2.0 * (c - b));
        return total;
    }
}
