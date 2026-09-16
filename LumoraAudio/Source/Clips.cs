// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A decoded clip: interleaved float PCM, whatever channel count and rate the file had. Players
// resample and upmix on read; the data is never touched after decode so any number of players can
// share one clip. -xlinka
public sealed class AudioClipData
{
    public AudioClipData(float[] samples, int channels, int sampleRate)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        Channels = channels;
        SampleRate = sampleRate;
    }

    public float[] Samples { get; }
    public int Channels { get; }
    public int SampleRate { get; }
    public int FrameCount => Samples.Length / Channels;
    public double Duration => (double)FrameCount / SampleRate;

    // Frame at an absolute index, upmixed or downmixed to stereo. Out of range reads silence.
    public StereoSample FrameAt(int frame)
    {
        if (frame < 0 || frame >= FrameCount) return default;
        int i = frame * Channels;
        return Channels switch
        {
            1 => new StereoSample(Samples[i]),
            2 => new StereoSample(Samples[i], Samples[i + 1]),
            _ => new StereoSample(Samples[i], Samples[i + 1]),
        };
    }
}
