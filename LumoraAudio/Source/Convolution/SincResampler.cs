// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Offline rate conversion by windowed sinc: 32 zero crossings a side under a Kaiser window (beta 8.6), the
// cutoff at 95% of the lower of the two Nyquists so going down in rate filters out what would fold back.
// The kernel is tabulated at 512 points per zero crossing and read by straight-line interpolation, which
// keeps it about 100 dB clean. For preparing a sound once, off the audio thread; it allocates its output.
internal static class SincResampler
{
    private const int ZeroCrossings = 32;
    private const int TableResolution = 512;
    private const double Beta = 8.6;
    private const double Rolloff = 0.95;

    private static readonly float[] Table = BuildTable();

    public static float[] Resample(ReadOnlySpan<float> input, int fromRate, int toRate)
    {
        if (fromRate <= 0 || toRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(fromRate));
        if (fromRate == toRate || input.Length == 0)
            return input.ToArray();

        double step = (double)fromRate / toRate;
        double cutoff = Rolloff * Math.Min(1.0, (double)toRate / fromRate);
        double reach = ZeroCrossings / cutoff;
        int length = (int)Math.Ceiling(input.Length / step);
        var output = new float[length];
        for (int j = 0; j < length; j++)
        {
            double center = j * step;
            int first = Math.Max(0, (int)Math.Ceiling(center - reach));
            int last = Math.Min(input.Length - 1, (int)Math.Floor(center + reach));
            double sum = 0.0;
            for (int k = first; k <= last; k++)
                sum += input[k] * Kernel((center - k) * cutoff);
            output[j] = (float)(sum * cutoff);
        }
        return output;
    }

    // sinc(x) under the window, for x in zero crossings.
    private static double Kernel(double x)
    {
        double at = Math.Abs(x) * TableResolution;
        int index = (int)at;
        if (index >= Table.Length - 1)
            return 0.0;
        double part = at - index;
        return Table[index] + part * (Table[index + 1] - Table[index]);
    }

    private static float[] BuildTable()
    {
        int count = ZeroCrossings * TableResolution + 2;
        var table = new float[count];
        double norm = 1.0 / SpectralWindow.BesselI0(Beta);
        for (int i = 0; i < count; i++)
        {
            double x = i / (double)TableResolution;
            double sinc = x == 0.0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            double r = x / ZeroCrossings;
            double window = r >= 1.0 ? 0.0 : SpectralWindow.BesselI0(Beta * Math.Sqrt(1.0 - r * r)) * norm;
            table[i] = (float)(sinc * window);
        }
        return table;
    }
}
