// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;

namespace Lumora.Audio;

// RNNoise over a whole clip. Each channel runs through its own network, at 48 kHz because that is the only
// rate the model knows: a clip at any other rate is resampled in and back out, and the resampler adds no
// delay. The network's output trails its input by NetworkDelay samples, so the input is run on past its
// end by that much and the head of the output is dropped.
//
// That lines the output up to within the phase of RNNoise's input DC blocker, a causal high-pass whose
// phase lead in the low band pulls speech about half a sample early against the original. The same blocker
// run backwards over the output cancels that phase at every frequency, so the cleaned clip lines up sample
// for sample, and the blocker's cut below 30 Hz counts twice. -xlinka
public static class ClipDenoiser
{
    // Half a window of overlap-add plus the frame RNNoise holds back for its pitch filter.
    public const int NetworkDelay = 2 * RnnoiseDenoiser.FrameSize;

    private const float ToPcm = 32768f;
    private const float FromPcm = 1f / 32768f;

    public static float[] Denoise(ReadOnlySpan<float> samples, int channels, int sampleRate)
    {
        int frames = ClipProcessing.FrameCount(samples, channels);
        ClipProcessing.RequireRate(sampleRate);
        var result = new float[samples.Length];
        if (frames == 0)
            return result;

        var split = new float[channels][];
        for (int c = 0; c < channels; c++)
        {
            var channel = new float[frames];
            for (int f = 0; f < frames; f++)
                channel[f] = samples[f * channels + c];
            split[c] = channel;
        }

        Parallel.For(0, channels, c => split[c] = DenoiseChannel(split[c], sampleRate));

        for (int c = 0; c < channels; c++)
        {
            var channel = split[c];
            for (int f = 0; f < frames; f++)
                result[f * channels + c] = channel[f];
        }
        return result;
    }

    private static float[] DenoiseChannel(float[] channel, int sampleRate)
    {
        bool native = sampleRate == RnnoiseDenoiser.SampleRate;
        float[] input = native ? channel : ClipResampler.Resample(channel, 1, sampleRate, RnnoiseDenoiser.SampleRate);

        int length = input.Length;
        int blocks = (length + NetworkDelay + RnnoiseDenoiser.FrameSize - 1) / RnnoiseDenoiser.FrameSize;
        var fed = new float[blocks * RnnoiseDenoiser.FrameSize];
        for (int i = 0; i < length; i++)
            fed[i] = input[i] * ToPcm;

        var cleaned = new float[fed.Length];
        var network = new RnnoiseDenoiser();
        for (int b = 0; b < blocks; b++)
        {
            int at = b * RnnoiseDenoiser.FrameSize;
            network.ProcessFrame(fed.AsSpan(at, RnnoiseDenoiser.FrameSize), cleaned.AsSpan(at, RnnoiseDenoiser.FrameSize));
        }

        var aligned = new float[length];
        for (int i = 0; i < length; i++)
            aligned[i] = cleaned[i + NetworkDelay] * FromPcm;
        UnwindBlockerPhase(aligned);

        return native ? aligned : ClipResampler.Resample(aligned, 1, RnnoiseDenoiser.SampleRate, sampleRate, channel.Length);
    }

    // RNNoise's DC blocker applied back to front, which conjugates its phase.
    private static void UnwindBlockerPhase(float[] x)
    {
        double m0 = 0.0;
        double m1 = 0.0;
        for (int i = x.Length - 1; i >= 0; i--)
        {
            double xi = x[i];
            double yi = xi + m0;
            m0 = m1 + (RnnoiseDenoiser.HpB0 * xi - RnnoiseDenoiser.HpA0 * yi);
            m1 = RnnoiseDenoiser.HpB1 * xi - RnnoiseDenoiser.HpA1 * yi;
            x[i] = (float)yi;
        }
    }
}
