// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Single numbers that describe where a spectrum's energy sits. DC is left out of all three: an offset is
// not a sound. A spectrum with no energy gives 0 for each.
public static class SpectralShape
{
    private const double Silence = 1e-24;

    // Hz: the magnitude-weighted mean frequency, the spectrum's centre of mass. Bright sounds sit high.
    public static float Centroid(ReadOnlySpan<float> magnitudes, float binWidth)
    {
        double weighted = 0.0;
        double total = 0.0;
        for (int k = 1; k < magnitudes.Length; k++)
        {
            weighted += (double)k * magnitudes[k];
            total += magnitudes[k];
        }
        return total > Silence ? (float)(weighted / total * binWidth) : 0f;
    }

    // Geometric over arithmetic mean of the powers: near 1 for noise, near 0 for a few clean tones.
    public static float Flatness(ReadOnlySpan<float> powers)
    {
        int count = powers.Length - 1;
        if (count <= 0)
            return 0f;
        double logSum = 0.0;
        double sum = 0.0;
        for (int k = 1; k < powers.Length; k++)
        {
            double p = powers[k];
            sum += p;
            logSum += Math.Log(p + Silence);
        }
        double mean = sum / count;
        if (mean <= Silence)
            return 0f;
        return (float)Math.Clamp(Math.Exp(logSum / count) / mean, 0.0, 1.0);
    }

    // Hz below which fraction of the power lies, interpolated inside the bin that crosses it.
    public static float Rolloff(ReadOnlySpan<float> powers, float binWidth, float fraction = 0.85f)
    {
        double total = 0.0;
        for (int k = 1; k < powers.Length; k++)
            total += powers[k];
        if (total <= Silence)
            return 0f;
        double target = total * Math.Clamp(fraction, 0f, 1f);
        double running = 0.0;
        for (int k = 1; k < powers.Length; k++)
        {
            double p = powers[k];
            if (running + p >= target && p > 0.0)
                return (float)((k - 0.5 + (target - running) / p) * binWidth);
            running += p;
        }
        return (float)((powers.Length - 1) * binWidth);
    }
}
