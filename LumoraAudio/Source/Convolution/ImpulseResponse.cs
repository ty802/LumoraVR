// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A room's impulse response made ready for a ConvolutionFilter: at the mixer's rate, cut into blocks and
// transformed, so the audio thread never does anything to it but read.
//
// Building one is the expensive part (a rate conversion and a transform per block) and belongs on a worker.
// One channel convolves both sides of the input; two convolve left with left and right with right; more
// than two keep channels 0 and 1. Trailing silence below -120 dB of the peak is cut, since every block kept
// costs every audio frame, and so is anything past MaxSeconds.
//
// Normalised by default to unit energy averaged over its channels, so noise goes in and comes out at the
// same level whatever the file's own gain: a raw room recording can carry 30 dB more energy than the sound
// it was made from, and Level is where a louder or quieter room is asked for. Each block's spectrum carries
// the 1/(2B) of the inverse transform, so the filter's inverse runs unscaled. -xlinka
public sealed class ImpulseResponse
{
    public const float MaxSeconds = 10f;
    public const int MaxBlockSize = 1024;
    public const int MinBlockSize = 32;

    private const float TrimFloor = 1e-6f;
    private const float FlushFloor = 1e-20f;

    // Real and imaginary spectra, [channel][partition][Stride], the bins past BlockSize left zero.
    internal readonly float[] Real;
    internal readonly float[] Imaginary;

    private ImpulseResponse(int sampleRate, int blockSize, int channels, int length, float gain, float[] real, float[] imaginary, int partitions, int stride)
    {
        SampleRate = sampleRate;
        BlockSize = blockSize;
        Channels = channels;
        Length = length;
        NormalizationGain = gain;
        Real = real;
        Imaginary = imaginary;
        Partitions = partitions;
        Stride = stride;
    }

    public int SampleRate { get; }

    // Samples per partition, which is also the filter's block and, when calls do not line up with it, its
    // latency.
    public int BlockSize { get; }

    public int Channels { get; }

    // Samples kept after trimming, at SampleRate.
    public int Length { get; }

    public int Partitions { get; }

    // Floats per partition spectrum: BlockSize + 1 bins rounded up to whole vectors of any width.
    public int Stride { get; }

    public float NormalizationGain { get; }

    public double Seconds => (double)Length / SampleRate;

    // Every bin past the last one that matters is zero, so a vector loop over the whole stride needs no tail.
    internal static int StrideFor(int blockSize) => (blockSize + 1 + 15) & ~15;

    // The partition size for a mixer frame: the frame itself when its double factors into small primes (every
    // call then covers whole blocks and the reverb comes out with no delay at all), else the largest such
    // divisor of it, else the largest power of two that fits.
    public static int BlockSizeFor(int frameSize)
    {
        int frame = Math.Max(MinBlockSize, frameSize);
        for (int d = Math.Min(frame, MaxBlockSize); d >= MinBlockSize; d--)
        {
            if (frame % d == 0 && IsSmooth(2 * d))
                return d;
        }
        int power = MinBlockSize;
        while (power * 2 <= Math.Min(frame, MaxBlockSize))
            power *= 2;
        return power;
    }

    public static ImpulseResponse FromClip(AudioClipData clip, int sampleRate, int blockSize, bool normalize = true)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));
        return Create(clip.Samples, clip.Channels, clip.SampleRate, sampleRate, blockSize, normalize);
    }

    // interleaved: frames of channels samples at sourceRate.
    public static ImpulseResponse Create(ReadOnlySpan<float> interleaved, int channels, int sourceRate, int sampleRate, int blockSize, bool normalize = true)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (sourceRate <= 0 || sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (blockSize < MinBlockSize || blockSize > MaxBlockSize)
            throw new ArgumentOutOfRangeException(nameof(blockSize), $"a block is {MinBlockSize} to {MaxBlockSize} samples");

        int used = channels >= 2 ? 2 : 1;
        int frames = interleaved.Length / channels;
        var split = new float[used][];
        for (int c = 0; c < used; c++)
        {
            var channel = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float v = interleaved[i * channels + c];
                channel[i] = float.IsFinite(v) ? v : 0f;
            }
            split[c] = sourceRate == sampleRate ? channel : SincResampler.Resample(channel, sourceRate, sampleRate);
        }

        int available = split[0].Length;
        float peak = 0f;
        for (int c = 0; c < used; c++)
        {
            foreach (float v in split[c])
                peak = MathF.Max(peak, MathF.Abs(v));
        }
        int length = 0;
        if (peak > 0f)
        {
            float floor = peak * TrimFloor;
            for (int c = 0; c < used; c++)
            {
                for (int i = available - 1; i >= length; i--)
                {
                    if (MathF.Abs(split[c][i]) > floor)
                    {
                        length = i + 1;
                        break;
                    }
                }
            }
        }
        length = Math.Min(length, (int)(MaxSeconds * sampleRate));

        float gain = 1f;
        if (normalize && length > 0)
        {
            double energy = 0.0;
            for (int c = 0; c < used; c++)
            {
                for (int i = 0; i < length; i++)
                    energy += (double)split[c][i] * split[c][i];
            }
            energy /= used;
            if (energy > 0.0)
                gain = (float)(1.0 / Math.Sqrt(energy));
        }

        int partitions = Math.Max(1, (length + blockSize - 1) / blockSize);
        int stride = StrideFor(blockSize);
        var real = new float[used * partitions * stride];
        var imaginary = new float[used * partitions * stride];
        var fft = RealFft.Get(2 * blockSize);
        var frame = new float[2 * blockSize];
        var re = new float[fft.BinCount];
        var im = new float[fft.BinCount];
        var scratch = new float[fft.ScratchLength];
        float scale = gain / (2 * blockSize);
        for (int c = 0; c < used; c++)
        {
            for (int p = 0; p < partitions; p++)
            {
                Array.Clear(frame);
                int from = p * blockSize;
                int count = Math.Min(blockSize, length - from);
                for (int i = 0; i < count; i++)
                {
                    float v = split[c][from + i];
                    frame[i] = MathF.Abs(v) < FlushFloor ? 0f : v;
                }
                fft.Forward(frame, re, im, scratch);
                int at = (c * partitions + p) * stride;
                for (int k = 0; k <= blockSize; k++)
                {
                    real[at + k] = Flush(re[k] * scale);
                    imaginary[at + k] = Flush(im[k] * scale);
                }
            }
        }
        return new ImpulseResponse(sampleRate, blockSize, used, length, gain, real, imaginary, partitions, stride);
    }

    private static float Flush(float value) => MathF.Abs(value) < 1e-30f ? 0f : value;

    private static bool IsSmooth(int n)
    {
        foreach (int p in new[] { 2, 3, 5, 7 })
        {
            while (n % p == 0)
                n /= p;
        }
        return n == 1;
    }
}
