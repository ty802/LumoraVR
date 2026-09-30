// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Lumora.Audio;

// Sample rate conversion for whole clips. Each output sample is a Kaiser-windowed sinc centred on its exact
// position in the input, so the result is linear phase with no delay: output frame j lines up with input
// time j * from / to. The kernel is tabulated per phase. Any two of the usual rates from 8 kHz to 192 kHz
// reduce to at most a few thousand phases, and those are tabulated exactly; an odd pair that needs more
// phases than the table holds interpolates linearly between neighbouring phases, fine enough to stay under
// the stopband.
//
// The transition band ends at the lower of the two Nyquist frequencies, so nothing above it folds back, and
// starts at PassbandFraction of it. At 44.1 kHz that is flat to 19.8 kHz. -xlinka
public static class ClipResampler
{
    public const double StopbandAttenuationDb = 120.0;
    public const double PassbandFraction = 0.9;

    private const int MaxPhases = 4096;
    private const int MaxTableFloats = 1 << 22;
    private const int BlockFrames = 1 << 15;

    // Frames a clip of inputFrames has at the new rate: its duration, rounded to the nearest frame.
    public static int OutputFrames(int inputFrames, int fromRate, int toRate)
    {
        ClipProcessing.RequireRate(fromRate);
        ClipProcessing.RequireRate(toRate);
        if (inputFrames <= 0)
            return 0;
        Reduce(fromRate, toRate, out int up, out int down);
        long frames = ((long)inputFrames * up + down / 2) / down;
        return frames > int.MaxValue ? int.MaxValue : (int)frames;
    }

    public static float[] Resample(ReadOnlySpan<float> samples, int channels, int fromRate, int toRate)
        => Resample(samples, channels, fromRate, toRate, -1);

    // outputFrames pins the length, for a round trip that has to land on the frame count it left from;
    // frames past the end of the input read silence. Negative takes OutputFrames.
    public static float[] Resample(ReadOnlySpan<float> samples, int channels, int fromRate, int toRate, int outputFrames)
    {
        int frames = ClipProcessing.FrameCount(samples, channels);
        ClipProcessing.RequireRate(fromRate);
        ClipProcessing.RequireRate(toRate);
        if (outputFrames < 0)
            outputFrames = OutputFrames(frames, fromRate, toRate);
        if (fromRate == toRate)
        {
            var copy = new float[outputFrames * channels];
            samples.Slice(0, Math.Min(samples.Length, copy.Length)).CopyTo(copy);
            return copy;
        }
        if (frames == 0 || outputFrames == 0)
            return new float[outputFrames * channels];

        if ((long)outputFrames * channels > int.MaxValue)
            throw new ArgumentException("the resampled clip would not fit in one buffer", nameof(samples));
        Reduce(fromRate, toRate, out int up, out int down);
        long overrun = ((long)outputFrames - 1) * down / up - frames + 1;
        if (overrun > frames + 1024)
            throw new ArgumentOutOfRangeException(nameof(outputFrames), outputFrames, "far more frames than the input can fill");
        var kernel = Kernel.For(fromRate, toRate);

        // Each channel is copied out once into a zero-padded run, so the kernel never has to test the clip's
        // ends and the dot product reads contiguous memory. The tail also covers a pinned length that runs
        // past the input.
        int pad = kernel.Stride + 1;
        int tail = pad + (int)Math.Max(0, overrun);
        var padded = new float[channels][];
        for (int c = 0; c < channels; c++)
        {
            var run = new float[pad + frames + tail];
            for (int f = 0; f < frames; f++)
                run[pad + f] = samples[f * channels + c];
            padded[c] = run;
        }

        var result = new float[outputFrames * channels];
        int blocks = (outputFrames + BlockFrames - 1) / BlockFrames;
        Parallel.For(0, blocks * channels, job =>
        {
            int c = job % channels;
            int from = job / channels * BlockFrames;
            int to = Math.Min(outputFrames, from + BlockFrames);
            kernel.Render(padded[c], pad, result, channels, c, from, to);
        });
        return result;
    }

    private static void Reduce(int fromRate, int toRate, out int up, out int down)
    {
        int a = fromRate;
        int b = toRate;
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }
        up = toRate / a;
        down = fromRate / a;
    }

    private sealed class Kernel
    {
        private readonly int _up;
        private readonly int _down;
        private readonly int _taps;
        private readonly int _phases;
        private readonly float[] _table;

        public int Stride { get; }

        private Kernel(int up, int down, int taps, int stride, int phases, float[] table)
        {
            _up = up;
            _down = down;
            _taps = taps;
            Stride = stride;
            _phases = phases;
            _table = table;
        }

        public static Kernel For(int fromRate, int toRate)
        {
            Reduce(fromRate, toRate, out int up, out int down);

            // All in cycles per input sample.
            double stop = 0.5 * Math.Min(1.0, (double)up / down);
            double pass = stop * PassbandFraction;
            double cutoff = 0.5 * (pass + stop);
            double transition = stop - pass;
            double beta = 0.1102 * (StopbandAttenuationDb - 8.7);
            double halfWidth = (StopbandAttenuationDb - 7.95) / (14.36 * transition) * 0.5;

            // One spare tap each side keeps the whole support inside the window for any phase in [0, 1].
            int taps = 2 * ((int)Math.Ceiling(halfWidth) + 1);
            int width = Vector<float>.Count;
            int stride = (taps + width - 1) / width * width;

            int phases = up;
            if (phases > MaxPhases || (long)(phases + 1) * stride > MaxTableFloats)
                phases = Math.Max(64, Math.Min(MaxPhases, MaxTableFloats / stride - 1));

            var table = new float[(phases + 1) * stride];
            var row = new double[taps];
            double besselBeta = BesselI0(beta);
            int centre = taps / 2 - 1;
            for (int p = 0; p <= phases; p++)
            {
                double offset = (double)p / phases;
                double sum = 0.0;
                for (int k = 0; k < taps; k++)
                {
                    double t = offset + centre - k;
                    double value = 0.0;
                    if (Math.Abs(t) <= halfWidth)
                    {
                        double x = 2.0 * cutoff * t;
                        double sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                        double r = t / halfWidth;
                        double window = BesselI0(beta * Math.Sqrt(Math.Max(0.0, 1.0 - r * r))) / besselBeta;
                        value = 2.0 * cutoff * sinc * window;
                    }
                    row[k] = value;
                    sum += value;
                }
                // Each phase sums to exactly one, so a constant passes through unchanged whatever the phase.
                double normalize = sum != 0.0 ? 1.0 / sum : 0.0;
                int at = p * stride;
                for (int k = 0; k < taps; k++)
                    table[at + k] = (float)(row[k] * normalize);
            }
            return new Kernel(up, down, taps, stride, phases, table);
        }

        public void Render(float[] input, int pad, float[] output, int channels, int channel, int from, int to)
        {
            var table = _table.AsSpan();
            int centre = _taps / 2 - 1;
            bool exact = _phases == _up;
            for (int j = from; j < to; j++)
            {
                long position = (long)j * _down;
                long whole = position / _up;
                int numerator = (int)(position - whole * _up);
                int start = (int)(whole - centre + pad);
                var window = input.AsSpan(start, Stride);

                float value;
                if (exact)
                {
                    value = Dot(window, table.Slice(numerator * Stride, Stride));
                }
                else
                {
                    double phase = (double)numerator * _phases / _up;
                    int p = (int)phase;
                    float fraction = (float)(phase - p);
                    float a = Dot(window, table.Slice(p * Stride, Stride));
                    float b = Dot(window, table.Slice((p + 1) * Stride, Stride));
                    value = a + (b - a) * fraction;
                }
                output[j * channels + channel] = value;
            }
        }

        private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            var sum = Vector<float>.Zero;
            int width = Vector<float>.Count;
            for (int i = 0; i < a.Length; i += width)
                sum += new Vector<float>(a.Slice(i, width)) * new Vector<float>(b.Slice(i, width));
            return Vector.Dot(sum, Vector<float>.One);
        }

        private static double BesselI0(double x)
        {
            double sum = 1.0;
            double term = 1.0;
            double quarter = x * x * 0.25;
            for (int k = 1; k < 500; k++)
            {
                term *= quarter / ((double)k * k);
                sum += term;
                if (term < sum * 1e-17)
                    break;
            }
            return sum;
        }
    }
}
