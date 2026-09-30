// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

public enum ClipFadeCurve
{
    Linear,
    // sin/cos pair: a crossfade between unrelated material holds its power through the middle.
    EqualPower,
    // Straight in decibels over 60 dB, pulled down to true silence at the quiet end.
    Exponential,
}

public enum ClipTrimEnds
{
    Both,
    Start,
    End,
}

// Offline edits of a whole decoded clip: interleaved float PCM with a channel count and a rate, the layout
// AudioClipData holds. Every edit reads its input and returns a new buffer, so a clip that players are
// sharing can be handed in as it is. Gains are worked out in double and each sample is rounded to float
// once, which is what puts a normalize on its target to the last bit a float can carry. -xlinka
public static class ClipProcessing
{
    // Bottom of the exponential fade, 60 dB down. The curve is shifted by this and rescaled so it starts
    // at exact silence instead of at -60 dB.
    private const double ExponentialFloor = 1e-3;

    public static double ToDecibels(double amplitude)
        => amplitude > 0.0 ? 20.0 * Math.Log10(amplitude) : double.NegativeInfinity;

    public static double FromDecibels(double decibels) => Math.Pow(10.0, decibels / 20.0);

    // LEVELS

    // The largest absolute sample. NaN reads as silence.
    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float magnitude = MathF.Abs(samples[i]);
            if (magnitude > peak)
                peak = magnitude;
        }
        return peak;
    }

    // Every channel counted together, so a full-scale sine reads -3.01 dBFS. Non-finite samples count as
    // silence.
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0.0;
        double sum = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            double value = samples[i];
            if (double.IsFinite(value))
                sum += value * value;
        }
        return Math.Sqrt(sum / samples.Length);
    }

    // GAIN

    public static float[] Gain(ReadOnlySpan<float> samples, double decibels)
        => Scale(samples, FromDecibels(decibels));

    // A silent clip has no gain that reaches the target and comes back unchanged.
    public static float[] NormalizePeak(ReadOnlySpan<float> samples, double targetDecibels)
    {
        float peak = Peak(samples);
        return Scale(samples, peak > 0f ? FromDecibels(targetDecibels) / peak : 1.0);
    }

    public static float[] NormalizeRms(ReadOnlySpan<float> samples, double targetDecibels)
    {
        double rms = Rms(samples);
        return Scale(samples, rms > 0.0 ? FromDecibels(targetDecibels) / rms : 1.0);
    }

    private static float[] Scale(ReadOnlySpan<float> samples, double gain)
    {
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            result[i] = (float)(samples[i] * gain);
        return result;
    }

    // TRIM

    // A frame is sound when any channel reaches the threshold. Silence at an end is cut only when it runs at
    // least minimumSilenceSeconds, and padSeconds of it is kept against the sound, since the soft onset of a
    // word or the tail of a decay sits under any threshold worth using. A clip that never reaches the
    // threshold comes back empty. -xlinka
    public static float[] TrimSilence(ReadOnlySpan<float> samples, int channels, int sampleRate, double thresholdDecibels,
        double minimumSilenceSeconds, ClipTrimEnds ends, double padSeconds)
    {
        int frames = FrameCount(samples, channels);
        RequireRate(sampleRate);
        float threshold = (float)FromDecibels(thresholdDecibels);

        int soundStart = -1;
        for (int f = 0; f < frames; f++)
        {
            if (Reaches(samples, f, channels, threshold))
            {
                soundStart = f;
                break;
            }
        }
        if (soundStart < 0)
            return Array.Empty<float>();

        int soundEnd = soundStart;
        for (int f = frames - 1; f > soundStart; f--)
        {
            if (Reaches(samples, f, channels, threshold))
            {
                soundEnd = f;
                break;
            }
        }

        int minimum = ToFrames(minimumSilenceSeconds, sampleRate);
        int pad = ToFrames(padSeconds, sampleRate);
        int start = 0;
        int end = frames;
        if (ends != ClipTrimEnds.End && soundStart >= minimum)
            start = Math.Max(0, soundStart - pad);
        if (ends != ClipTrimEnds.Start && frames - 1 - soundEnd >= minimum)
            end = (int)Math.Min(frames, (long)soundEnd + 1 + pad);
        return Slice(samples, channels, start, end);
    }

    // Seconds off each end, rounded to the nearest frame. Cutting more than there is leaves nothing.
    public static float[] Trim(ReadOnlySpan<float> samples, int channels, int sampleRate, double startSeconds, double endSeconds)
    {
        int frames = FrameCount(samples, channels);
        RequireRate(sampleRate);
        int start = Math.Min(frames, ToFrames(startSeconds, sampleRate));
        int end = Math.Max(start, frames - Math.Min(frames, ToFrames(endSeconds, sampleRate)));
        return Slice(samples, channels, start, end);
    }

    // FADES

    // Gain of a fade in at t through it, 0 at t = 0 and 1 from t = 1 on. A fade out plays the same curve
    // backwards.
    public static double FadeGain(ClipFadeCurve curve, double t)
    {
        if (!(t > 0.0))
            return 0.0;
        if (t >= 1.0)
            return 1.0;
        return curve switch
        {
            ClipFadeCurve.Linear => t,
            ClipFadeCurve.EqualPower => Math.Sin(t * (Math.PI * 0.5)),
            _ => (Math.Pow(10.0, 3.0 * (t - 1.0)) - ExponentialFloor) / (1.0 - ExponentialFloor),
        };
    }

    // Frame i of an n-frame fade in takes FadeGain(i / n): the opening frame is silent and the frame after
    // the fade is untouched. A fade longer than the clip covers the whole clip.
    public static float[] FadeIn(ReadOnlySpan<float> samples, int channels, int sampleRate, double seconds, ClipFadeCurve curve)
    {
        int frames = FrameCount(samples, channels);
        RequireRate(sampleRate);
        int length = Math.Min(frames, ToFrames(seconds, sampleRate));
        var result = samples.ToArray();
        for (int f = 0; f < length; f++)
            ApplyGain(result, f, channels, FadeGain(curve, (double)f / length));
        return result;
    }

    // The mirror image of FadeIn: reversing, fading in and reversing back gives the same samples, so the
    // closing frame is silent.
    public static float[] FadeOut(ReadOnlySpan<float> samples, int channels, int sampleRate, double seconds, ClipFadeCurve curve)
    {
        int frames = FrameCount(samples, channels);
        RequireRate(sampleRate);
        int length = Math.Min(frames, ToFrames(seconds, sampleRate));
        var result = samples.ToArray();
        int from = frames - length;
        for (int j = 0; j < length; j++)
            ApplyGain(result, from + j, channels, FadeGain(curve, (double)(length - 1 - j) / length));
        return result;
    }

    // LOOP

    // The last crossfadeSeconds of the clip are faded out over its opening while the opening fades in, and
    // the tail is dropped. Frame 0 of the result is exactly the frame that followed the new end in the
    // original, so when playback wraps it continues the waveform it was already playing: no jump and no
    // change of slope at the seam, only the crossfade at the head where both halves overlap. The overlap is
    // held to half the clip. -xlinka
    public static float[] MakeLoopable(ReadOnlySpan<float> samples, int channels, int sampleRate, double crossfadeSeconds,
        ClipFadeCurve curve = ClipFadeCurve.EqualPower)
    {
        int frames = FrameCount(samples, channels);
        RequireRate(sampleRate);
        int overlap = Math.Min(frames / 2, ToFrames(crossfadeSeconds, sampleRate));
        if (overlap <= 0)
            return samples.ToArray();

        int length = frames - overlap;
        var result = new float[length * channels];
        samples.Slice(0, result.Length).CopyTo(result);
        for (int f = 0; f < overlap; f++)
        {
            double t = (double)f / overlap;
            double fadeIn = FadeGain(curve, t);
            double fadeOut = FadeGain(curve, 1.0 - t);
            int head = f * channels;
            int tail = (length + f) * channels;
            for (int c = 0; c < channels; c++)
                result[head + c] = (float)(samples[head + c] * fadeIn + samples[tail + c] * fadeOut);
        }
        return result;
    }

    // CHANNELS

    // The average of every channel, one channel out.
    public static float[] ToMono(ReadOnlySpan<float> samples, int channels)
    {
        int frames = FrameCount(samples, channels);
        var result = new float[frames];
        double scale = 1.0 / channels;
        for (int f = 0; f < frames; f++)
        {
            int at = f * channels;
            double sum = 0.0;
            for (int c = 0; c < channels; c++)
                sum += samples[at + c];
            result[f] = (float)(sum * scale);
        }
        return result;
    }

    // Mid/side on the front pair, channels 0 and 1: mid = (L + R) / 2 on both, sides = (L - R) / 2 on the
    // left and its negative on the right, so the two extractions add back up to the original. Channels past
    // the pair stay with the mid. A mono clip is all mid and has no sides.
    public static float[] ExtractMid(ReadOnlySpan<float> samples, int channels)
    {
        int frames = FrameCount(samples, channels);
        var result = samples.ToArray();
        if (channels < 2)
            return result;
        for (int f = 0; f < frames; f++)
        {
            int at = f * channels;
            float mid = (float)(((double)samples[at] + samples[at + 1]) * 0.5);
            result[at] = mid;
            result[at + 1] = mid;
        }
        return result;
    }

    public static float[] ExtractSides(ReadOnlySpan<float> samples, int channels)
    {
        int frames = FrameCount(samples, channels);
        var result = new float[samples.Length];
        if (channels < 2)
            return result;
        for (int f = 0; f < frames; f++)
        {
            int at = f * channels;
            float side = (float)(((double)samples[at] - samples[at + 1]) * 0.5);
            result[at] = side;
            result[at + 1] = -side;
        }
        return result;
    }

    // Left and right trade places; anything past the front pair stays put.
    public static float[] SwapChannels(ReadOnlySpan<float> samples, int channels)
    {
        int frames = FrameCount(samples, channels);
        var result = samples.ToArray();
        if (channels < 2)
            return result;
        for (int f = 0; f < frames; f++)
        {
            int at = f * channels;
            result[at] = samples[at + 1];
            result[at + 1] = samples[at];
        }
        return result;
    }

    // Frames in reverse order, channels within a frame as they were.
    public static float[] Reverse(ReadOnlySpan<float> samples, int channels)
    {
        int frames = FrameCount(samples, channels);
        var result = new float[samples.Length];
        for (int f = 0; f < frames; f++)
            samples.Slice(f * channels, channels).CopyTo(result.AsSpan((frames - 1 - f) * channels, channels));
        return result;
    }

    // HELPERS

    // Seconds to whole frames, rounded, never negative. NaN counts as none.
    public static int ToFrames(double seconds, int sampleRate)
    {
        if (!(seconds > 0.0))
            return 0;
        double frames = Math.Round(seconds * sampleRate);
        return frames >= int.MaxValue ? int.MaxValue : (int)frames;
    }

    internal static int FrameCount(ReadOnlySpan<float> samples, int channels)
    {
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "a clip needs at least one channel");
        if (samples.Length % channels != 0)
            throw new ArgumentException($"{samples.Length} samples is not a whole number of {channels}-channel frames", nameof(samples));
        return samples.Length / channels;
    }

    internal static void RequireRate(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "a sample rate must be positive");
    }

    private static bool Reaches(ReadOnlySpan<float> samples, int frame, int channels, float threshold)
    {
        int at = frame * channels;
        for (int c = 0; c < channels; c++)
        {
            if (MathF.Abs(samples[at + c]) >= threshold)
                return true;
        }
        return false;
    }

    private static void ApplyGain(float[] samples, int frame, int channels, double gain)
    {
        int at = frame * channels;
        for (int c = 0; c < channels; c++)
            samples[at + c] = (float)(samples[at + c] * gain);
    }

    private static float[] Slice(ReadOnlySpan<float> samples, int channels, int startFrame, int endFrame)
        => endFrame <= startFrame
            ? Array.Empty<float>()
            : samples.Slice(startFrame * channels, (endFrame - startFrame) * channels).ToArray();
}
