// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Window shapes and what they do to a spectrum.
//
// Periodic is the one to transform with: it is the symmetric window one sample longer with the last sample
// dropped, so its DFT has exactly the lobes the shape is known for and it overlap-adds to a flat line at the
// hops that shape allows. Symmetric peaks at the middle and is the filter-design convention.
//
// Every cosine shape here is a sum of cos(2 pi k n / D) terms with alternating signs; a hop of D / R makes
// all of them cancel across the overlap once R is past the highest k, which is why Hann and Hamming are
// flat at a half-frame hop, Blackman at a third, Blackman-Harris at a quarter and the flat-top at a fifth.
public static class SpectralWindow
{
    public const float DefaultKaiserBeta = 8.6f;

    private static readonly double[] HannTerms = { 0.5, 0.5 };
    private static readonly double[] HammingTerms = { 0.54, 0.46 };
    private static readonly double[] BlackmanTerms = { 0.42, 0.5, 0.08 };
    private static readonly double[] BlackmanHarrisTerms = { 0.35875, 0.48829, 0.14128, 0.01168 };
    private static readonly double[] FlatTopTerms = { 0.21557895, 0.41663158, 0.277263158, 0.083578947, 0.006947368 };

    public static float[] Create(int length, WindowShape shape, bool periodic = true, float kaiserBeta = DefaultKaiserBeta)
    {
        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length), "a window needs at least one sample");
        var window = new float[length];
        Fill(window, shape, periodic, kaiserBeta);
        return window;
    }

    public static void Fill(Span<float> window, WindowShape shape, bool periodic = true, float kaiserBeta = DefaultKaiserBeta)
    {
        int n = window.Length;
        if (n == 0)
            return;
        if (n == 1 || shape == WindowShape.Rectangular)
        {
            window.Fill(1f);
            return;
        }
        double span = periodic ? n : n - 1;
        if (shape == WindowShape.Kaiser)
        {
            double beta = float.IsFinite(kaiserBeta) ? Math.Abs(kaiserBeta) : DefaultKaiserBeta;
            double norm = 1.0 / BesselI0(beta);
            for (int i = 0; i < n; i++)
            {
                double x = 2.0 * i / span - 1.0;
                double root = Math.Sqrt(Math.Max(0.0, 1.0 - x * x));
                window[i] = (float)(BesselI0(beta * root) * norm);
            }
            return;
        }
        var terms = TermsOf(shape);
        for (int i = 0; i < n; i++)
        {
            double sum = terms[0];
            double sign = -1.0;
            for (int k = 1; k < terms.Length; k++)
            {
                sum += sign * terms[k] * Math.Cos(2.0 * Math.PI * k * i / span);
                sign = -sign;
            }
            window[i] = (float)sum;
        }
    }

    // The mean of the window: a sine centred on a bin comes out at half its amplitude times the window's sum.
    public static double CoherentGain(ReadOnlySpan<float> window)
    {
        double sum = 0.0;
        foreach (float w in window)
            sum += w;
        return window.Length == 0 ? 0.0 : sum / window.Length;
    }

    // In bins: how wide a rectangle passes the same noise power as the window at the same peak gain.
    public static double NoiseBandwidth(ReadOnlySpan<float> window)
    {
        double sum = 0.0;
        double squares = 0.0;
        foreach (float w in window)
        {
            sum += w;
            squares += (double)w * w;
        }
        return sum == 0.0 ? 0.0 : window.Length * squares / (sum * sum);
    }

    // What overlap-adding analysis times synthesis at a hop sums to, at each of the hop's positions. An empty
    // synthesis window is plain overlap-add.
    public static void OverlapSum(ReadOnlySpan<float> analysis, ReadOnlySpan<float> synthesis, int hop, Span<float> sum)
    {
        if (hop < 1 || sum.Length < hop)
            throw new ArgumentException("the sum needs one value per sample of the hop");
        bool plain = synthesis.IsEmpty;
        if (!plain && synthesis.Length != analysis.Length)
            throw new ArgumentException("the two windows differ in length");
        for (int i = 0; i < hop; i++)
        {
            double total = 0.0;
            for (int at = i; at < analysis.Length; at += hop)
                total += plain ? analysis[at] : (double)analysis[at] * synthesis[at];
            sum[i] = (float)total;
        }
    }

    // Constant overlap-add: the window's overlap sum at this hop is flat to within tolerance of its mean,
    // which is the gain plain overlap-add puts on the signal.
    public static bool IsCola(ReadOnlySpan<float> window, int hop, out float gain, float tolerance = 1e-5f)
    {
        Span<float> sum = hop <= 4096 ? stackalloc float[hop] : new float[hop];
        OverlapSum(window, default, hop, sum);
        double mean = 0.0;
        foreach (float v in sum)
            mean += v;
        mean /= hop;
        gain = (float)mean;
        if (mean == 0.0)
            return false;
        foreach (float v in sum)
        {
            if (Math.Abs(v - mean) > tolerance * Math.Abs(mean))
                return false;
        }
        return true;
    }

    // I0, the modified Bessel function of order zero, by its power series.
    public static double BesselI0(double x)
    {
        double quarter = x * x * 0.25;
        double term = 1.0;
        double sum = 1.0;
        for (int k = 1; k < 500; k++)
        {
            term *= quarter / ((double)k * k);
            sum += term;
            if (term < sum * 1e-17)
                break;
        }
        return sum;
    }

    private static double[] TermsOf(WindowShape shape) => shape switch
    {
        WindowShape.Hamming => HammingTerms,
        WindowShape.Blackman => BlackmanTerms,
        WindowShape.BlackmanHarris => BlackmanHarrisTerms,
        WindowShape.FlatTop => FlatTopTerms,
        _ => HannTerms,
    };
}
