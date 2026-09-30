// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// pitch.c: Copyright (c) 2007-2008 CSIRO, Copyright (c) 2007-2009 Xiph.Org Foundation,
// written by Jean-Marc Valin.
// celt_lpc.c: Copyright (c) 2009-2010 Xiph.Org Foundation, written by Jean-Marc Valin.
// See LICENSE-RNNoise.txt.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lumora.Audio;

// Scratch for one denoiser's pitch analysis, sized for RNNoise's 960-sample pitch frame and
// 768-sample maximum period.
internal sealed class RnnoisePitchScratch
{
    internal readonly float[] XLp4 = new float[RnnoisePitch.FrameSize >> 2];
    internal readonly float[] YLp4 = new float[(RnnoisePitch.FrameSize + RnnoisePitch.MaxPeriod) >> 2];
    internal readonly float[] XCorr = new float[RnnoisePitch.MaxPeriod >> 1];
    internal readonly float[] YyLookup = new float[RnnoisePitch.MaxPeriod + 1];
}

// The float build of CELT's pitch search as RNNoise uses it. Every correlation is summed in
// sample order, one accumulator per lag, like xcorr_kernel and celt_inner_prod; the vector paths
// only run several lags side by side.
internal static class RnnoisePitch
{
    internal const int MinPeriod = 60;
    internal const int MaxPeriod = 768;
    internal const int FrameSize = 960;
    internal const int BufferSize = MaxPeriod + FrameSize;

    private static ReadOnlySpan<int> SecondCheck => [0, 0, 3, 2, 3, 2, 5, 2, 3, 2, 3, 2, 5, 2, 3, 2];

    // rnn_pitch_downsample for one channel: 2x decimation, then a 4th order LPC whitening filter
    // with an added zero. x holds len samples, xLp receives len / 2.
    internal static void Downsample(ReadOnlySpan<float> x, Span<float> xLp, int len)
    {
        int half = len >> 1;
        for (int i = 1; i < half; i++)
            xLp[i] = .5f * (.5f * (x[2 * i - 1] + x[2 * i + 1]) + x[2 * i]);
        xLp[0] = .5f * (.5f * x[1] + x[0]);

        Span<float> ac = stackalloc float[5];
        Autocorr(xLp.Slice(0, half), ac, 4);

        // Noise floor -40 dB, then lag windowing.
        ac[0] *= 1.0001f;
        for (int i = 1; i <= 4; i++)
            ac[i] -= ac[i] * (.008f * i) * (.008f * i);

        Span<float> lpc = stackalloc float[4];
        Lpc(lpc, ac, 4);
        float tmp = 1f;
        for (int i = 0; i < 4; i++)
        {
            tmp = .9f * tmp;
            lpc[i] = lpc[i] * tmp;
        }

        const float c1 = .8f;
        float lpc0 = lpc[0] + .8f;
        float lpc1 = lpc[1] + c1 * lpc[0];
        float lpc2 = lpc[2] + c1 * lpc[1];
        float lpc3 = lpc[3] + c1 * lpc[2];
        float lpc4 = c1 * lpc[3];
        Fir5(xLp.Slice(0, half), lpc0, lpc1, lpc2, lpc3, lpc4);
    }

    // celt_fir5 in place, zero initial memory.
    private static void Fir5(Span<float> x, float num0, float num1, float num2, float num3, float num4)
    {
        float mem0 = 0, mem1 = 0, mem2 = 0, mem3 = 0, mem4 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float sum = x[i];
            sum = sum + num0 * mem0;
            sum = sum + num1 * mem1;
            sum = sum + num2 * mem2;
            sum = sum + num3 * mem3;
            sum = sum + num4 * mem4;
            mem4 = mem3;
            mem3 = mem2;
            mem2 = mem1;
            mem1 = mem0;
            mem0 = x[i];
            x[i] = sum;
        }
    }

    // rnn_autocorr without a window: lags 0..lag over all of x.
    private static void Autocorr(ReadOnlySpan<float> x, Span<float> ac, int lag)
    {
        int n = x.Length;
        int fastN = n - lag;
        Xcorr(x, x, ac, fastN, lag + 1);
        for (int k = 0; k <= lag; k++)
        {
            float d = 0;
            for (int i = k + fastN; i < n; i++)
                d = d + x[i] * x[i - k];
            ac[k] += d;
        }
    }

    // rnn_lpc, float build: Levinson-Durbin, stopping once the error drops 30 dB.
    private static void Lpc(Span<float> lpc, ReadOnlySpan<float> ac, int p)
    {
        float error = ac[0];
        lpc.Slice(0, p).Clear();
        if (ac[0] == 0)
            return;
        for (int i = 0; i < p; i++)
        {
            float rr = 0;
            for (int j = 0; j < i; j++)
                rr += lpc[j] * ac[i - j];
            rr += ac[i + 1];
            float r = -rr / error;
            lpc[i] = r;
            for (int j = 0; j < (i + 1) >> 1; j++)
            {
                float tmp1 = lpc[j];
                float tmp2 = lpc[i - 1 - j];
                lpc[j] = tmp1 + r * tmp2;
                lpc[i - 1 - j] = tmp2 + r * tmp1;
            }
            error = error - r * r * error;
            if (error < .001f * ac[0])
                break;
        }
    }

    // rnn_pitch_xcorr: xcorr[i] = sum over j < len of x[j] * y[i + j], for i < maxPitch.
    internal static void Xcorr(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> xcorr, int len, int maxPitch)
    {
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float xr = ref MemoryMarshal.GetReference(x);
            ref float yr = ref MemoryMarshal.GetReference(y);
            ref float cr = ref MemoryMarshal.GetReference(xcorr);
            for (; i + 16 <= maxPitch; i += 16)
            {
                Vector128<float> a0 = Vector128<float>.Zero;
                Vector128<float> a1 = Vector128<float>.Zero;
                Vector128<float> a2 = Vector128<float>.Zero;
                Vector128<float> a3 = Vector128<float>.Zero;
                for (int j = 0; j < len; j++)
                {
                    Vector128<float> xj = Vector128.Create(Unsafe.Add(ref xr, j));
                    nuint at = (nuint)(i + j);
                    a0 += xj * Vector128.LoadUnsafe(ref yr, at);
                    a1 += xj * Vector128.LoadUnsafe(ref yr, at + 4);
                    a2 += xj * Vector128.LoadUnsafe(ref yr, at + 8);
                    a3 += xj * Vector128.LoadUnsafe(ref yr, at + 12);
                }
                a0.StoreUnsafe(ref cr, (nuint)i);
                a1.StoreUnsafe(ref cr, (nuint)(i + 4));
                a2.StoreUnsafe(ref cr, (nuint)(i + 8));
                a3.StoreUnsafe(ref cr, (nuint)(i + 12));
            }
            for (; i + 4 <= maxPitch; i += 4)
            {
                Vector128<float> a0 = Vector128<float>.Zero;
                for (int j = 0; j < len; j++)
                    a0 += Vector128.Create(Unsafe.Add(ref xr, j)) * Vector128.LoadUnsafe(ref yr, (nuint)(i + j));
                a0.StoreUnsafe(ref cr, (nuint)i);
            }
        }
        // Four lags at a time like xcorr_kernel, then one at a time.
        for (; i + 4 <= maxPitch; i += 4)
        {
            float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int j = 0; j < len; j++)
            {
                float xj = x[j];
                s0 = s0 + xj * y[i + j];
                s1 = s1 + xj * y[i + j + 1];
                s2 = s2 + xj * y[i + j + 2];
                s3 = s3 + xj * y[i + j + 3];
            }
            xcorr[i] = s0;
            xcorr[i + 1] = s1;
            xcorr[i + 2] = s2;
            xcorr[i + 3] = s3;
        }
        for (; i < maxPitch; i++)
            xcorr[i] = InnerProduct(x, y.Slice(i), len);
    }


    private static float InnerProduct(ReadOnlySpan<float> x, ReadOnlySpan<float> y, int n)
    {
        float xy = 0;
        for (int i = 0; i < n; i++)
            xy = xy + x[i] * y[i];
        return xy;
    }

    private static void DualInnerProduct(ReadOnlySpan<float> x, ReadOnlySpan<float> y1, ReadOnlySpan<float> y2, int n, out float xy1, out float xy2)
    {
        float a = 0;
        float b = 0;
        for (int i = 0; i < n; i++)
        {
            a = a + x[i] * y1[i];
            b = b + x[i] * y2[i];
        }
        xy1 = a;
        xy2 = b;
    }

    // find_best_pitch, float build: the two lags with the best normalized correlation.
    private static void FindBestPitch(ReadOnlySpan<float> xcorr, ReadOnlySpan<float> y, int len, int maxPitch, out int best0, out int best1)
    {
        float syy = 1;
        float bestNum0 = -1, bestNum1 = -1;
        float bestDen0 = 0, bestDen1 = 0;
        best0 = 0;
        best1 = 1;
        for (int j = 0; j < len; j++)
            syy = syy + y[j] * y[j];
        for (int i = 0; i < maxPitch; i++)
        {
            if (xcorr[i] > 0)
            {
                // Scaled so squaring stays clear of both underflow and overflow.
                float xcorr16 = xcorr[i];
                xcorr16 *= 1e-12f;
                float num = xcorr16 * xcorr16;
                if (num * bestDen1 > bestNum1 * syy)
                {
                    if (num * bestDen0 > bestNum0 * syy)
                    {
                        bestNum1 = bestNum0;
                        bestDen1 = bestDen0;
                        best1 = best0;
                        bestNum0 = num;
                        bestDen0 = syy;
                        best0 = i;
                    }
                    else
                    {
                        bestNum1 = num;
                        bestDen1 = syy;
                        best1 = i;
                    }
                }
            }
            syy += y[i + len] * y[i + len] - y[i] * y[i];
            syy = 1 > syy ? 1 : syy;
        }
    }

    // rnn_pitch_search: coarse search at 4x decimation, refinement at 2x around the two best
    // coarse lags, then pseudo-interpolation. Returns the lag in 2x-decimated samples times two.
    internal static int Search(ReadOnlySpan<float> xLp, ReadOnlySpan<float> y, int len, int maxPitch, RnnoisePitchScratch scratch)
    {
        int lag = len + maxPitch;
        Span<float> xLp4 = scratch.XLp4.AsSpan(0, len >> 2);
        Span<float> yLp4 = scratch.YLp4.AsSpan(0, lag >> 2);
        Span<float> xcorr = scratch.XCorr.AsSpan(0, maxPitch >> 1);

        for (int j = 0; j < len >> 2; j++)
            xLp4[j] = xLp[2 * j];
        for (int j = 0; j < lag >> 2; j++)
            yLp4[j] = y[2 * j];

        Xcorr(xLp4, yLp4, xcorr, len >> 2, maxPitch >> 2);
        FindBestPitch(xcorr, yLp4, len >> 2, maxPitch >> 2, out int best0, out int best1);

        int half = maxPitch >> 1;
        xcorr.Clear();
        FineCorrelation(xLp, y, xcorr, len >> 1, 2 * best0);
        FineCorrelation(xLp, y, xcorr, len >> 1, 2 * best1);
        FindBestPitch(xcorr, y, len >> 1, half, out best0, out _);

        int offset = 0;
        if (best0 > 0 && best0 < half - 1)
        {
            float a = xcorr[best0 - 1];
            float b = xcorr[best0];
            float c = xcorr[best0 + 1];
            if (c - a > .7f * (b - a))
                offset = 1;
            else if (a - c > .7f * (b - c))
                offset = -1;
        }
        return 2 * best0 - offset;
    }

    // The lags within 2 of center, each max(-1, correlation); lags the C loop skips stay 0.
    private static void FineCorrelation(ReadOnlySpan<float> xLp, ReadOnlySpan<float> y, Span<float> xcorr, int len, int center)
    {
        int lo = Math.Max(0, center - 2);
        int hi = Math.Min(xcorr.Length - 1, center + 2);
        if (lo > hi)
            return;
        Xcorr(xLp, y.Slice(lo), xcorr.Slice(lo, hi - lo + 1), len, hi - lo + 1);
        for (int i = lo; i <= hi; i++)
            xcorr[i] = -1 > xcorr[i] ? -1 : xcorr[i];
    }

    private static float PitchGain(float xy, float xx, float yy) => (float)(xy / Math.Sqrt(1 + xx * yy));

    // rnn_remove_doubling: checks whether a submultiple of the period explains the signal as
    // well, favouring continuity with the previous frame. x holds maxPeriod + n 2x-decimated
    // samples; period is in full-rate samples on the way in and out. Returns the pitch gain.
    internal static float RemoveDoubling(ReadOnlySpan<float> x, int maxPeriod, int minPeriod, int n, ref int period, int prevPeriod, float prevGain, RnnoisePitchScratch scratch)
    {
        int minPeriod0 = minPeriod;
        maxPeriod /= 2;
        minPeriod /= 2;
        int t0 = period / 2;
        prevPeriod /= 2;
        n /= 2;
        if (t0 >= maxPeriod)
            t0 = maxPeriod - 1;

        // x[k] here is x[maxPeriod + k] in the C code, where x was advanced by maxPeriod.
        ReadOnlySpan<float> cur = x.Slice(maxPeriod);
        Span<float> yyLookup = scratch.YyLookup;

        int t = t0;
        DualInnerProduct(cur, cur, x.Slice(maxPeriod - t0), n, out float xx, out float xy);
        yyLookup[0] = xx;
        float yy = xx;
        for (int i = 1; i <= maxPeriod; i++)
        {
            float a = x[maxPeriod - i];
            float b = cur[n - i];
            yy = yy + a * a - b * b;
            yyLookup[i] = 0 > yy ? 0 : yy;
        }
        yy = yyLookup[t0];
        float bestXy = xy;
        float bestYy = yy;
        float g0 = PitchGain(xy, xx, yy);
        float g = g0;

        // Look for a pitch at t0 / k.
        for (int k = 2; k <= 15; k++)
        {
            int t1 = (2 * t0 + k) / (2 * k);
            if (t1 < minPeriod)
                break;
            int t1b;
            if (k == 2)
                t1b = t1 + t0 > maxPeriod ? t0 : t0 + t1;
            else
                t1b = (2 * SecondCheck[k] * t0 + k) / (2 * k);
            DualInnerProduct(cur, x.Slice(maxPeriod - t1), x.Slice(maxPeriod - t1b), n, out xy, out float xy2);
            xy = .5f * (xy + xy2);
            yy = .5f * (yyLookup[t1] + yyLookup[t1b]);
            float g1 = PitchGain(xy, xx, yy);
            float cont;
            if (Math.Abs(t1 - prevPeriod) <= 1)
                cont = prevGain;
            else if (Math.Abs(t1 - prevPeriod) <= 2 && 5 * k * k < t0)
                cont = .5f * prevGain;
            else
                cont = 0;
            float thresh = Max(.3f, .7f * g0 - cont);
            // Bias against very short periods, where short-term correlation fakes a pitch.
            if (t1 < 3 * minPeriod)
                thresh = Max(.4f, .85f * g0 - cont);
            else if (t1 < 2 * minPeriod)
                thresh = Max(.5f, .9f * g0 - cont);
            if (g1 > thresh)
            {
                bestXy = xy;
                bestYy = yy;
                t = t1;
                g = g1;
            }
        }

        bestXy = 0 > bestXy ? 0 : bestXy;
        float pg = bestYy <= bestXy ? 1f : bestXy / (bestYy + 1);

        Span<float> xcorr = stackalloc float[3];
        for (int k = 0; k < 3; k++)
            xcorr[k] = InnerProduct(cur, x.Slice(maxPeriod - (t + k - 1)), n);
        int offset;
        if (xcorr[2] - xcorr[0] > .7f * (xcorr[1] - xcorr[0]))
            offset = 1;
        else if (xcorr[0] - xcorr[2] > .7f * (xcorr[1] - xcorr[2]))
            offset = -1;
        else
            offset = 0;
        if (pg > g)
            pg = g;
        period = 2 * t + offset;
        if (period < minPeriod0)
            period = minPeriod0;
        return pg;
    }

    private static float Max(float a, float b) => a > b ? a : b;
}
