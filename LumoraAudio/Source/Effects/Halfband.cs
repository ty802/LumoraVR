// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// Half-band lowpass FIRs for changing rate by two, designed once as a Kaiser-windowed sinc.
//
// A half-band filter cuts at a quarter of the rate it runs at, and its taps two, four, six... away from the centre
// are exactly zero while the centre is exactly a half. So each rate change splits into two branches: one is the
// odd taps as a short FIR, the other is a plain delay. Taps is 4m + 3 so the half-length M is odd and the delay
// branch lands on a whole sample. The odd taps are scaled to add up to a half, which with the centre makes DC pass
// at exactly unity. -xlinka
internal static class Halfband
{
    // The M + 1 odd taps, from offset -M to M, of a filter `taps` long with the given stopband in decibels.
    public static float[] Design(int taps, double attenuationDb)
    {
        int half = (taps - 1) / 2;
        double beta = 0.1102 * (attenuationDb - 8.7);
        double norm = BesselI0(beta);
        var odd = new double[half + 1];
        double sum = 0.0;
        for (int i = 0; i <= half; i++)
        {
            int offset = 2 * i - half;
            double ratio = offset / (double)half;
            double window = BesselI0(beta * Math.Sqrt(Math.Max(0.0, 1.0 - ratio * ratio))) / norm;
            double sinc = Math.Sin(Math.PI * offset / 2.0) / (Math.PI * offset);
            odd[i] = sinc * window;
            sum += odd[i];
        }
        var coefficients = new float[half + 1];
        for (int i = 0; i <= half; i++)
            coefficients[i] = (float)(odd[i] * 0.5 / sum);
        return coefficients;
    }

    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int count = a.Length;
        int i = 0;
        float sum = 0f;
        if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            var wideA = MemoryMarshal.Cast<float, Vector<float>>(a);
            var wideB = MemoryMarshal.Cast<float, Vector<float>>(b);
            var accumulator = Vector<float>.Zero;
            for (int v = 0; v < wideA.Length; v++)
                accumulator += wideA[v] * wideB[v];
            sum = Vector.Sum(accumulator);
            i = wideA.Length * Vector<float>.Count;
        }
        for (; i < count; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static double BesselI0(double x)
    {
        double sum = 1.0;
        double term = 1.0;
        double quarter = x * x / 4.0;
        for (int k = 1; k < 64; k++)
        {
            term *= quarter / (k * (double)k);
            sum += term;
            if (term < sum * 1e-16)
                break;
        }
        return sum;
    }
}
