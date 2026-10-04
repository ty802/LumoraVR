// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Audio.Wasapi;

// How one frame of a device's own format lies in memory, and how to read it as a stereo pair. Shared
// mode is float nearly everywhere, but a capture device can still hand over integer PCM, and a surround
// output hands over more than two channels: the centre is folded into both sides so dialogue in a film
// does not vanish, the rest is left out. -xlinka
internal readonly struct WasapiSampleLayout
{
    public readonly int Channels;
    public readonly int BytesPerSample;
    public readonly int BlockAlign;
    public readonly int SampleRate;
    public readonly bool IsFloat;

    private WasapiSampleLayout(int channels, int bytesPerSample, int blockAlign, int sampleRate, bool isFloat)
    {
        Channels = channels;
        BytesPerSample = bytesPerSample;
        BlockAlign = blockAlign;
        SampleRate = sampleRate;
        IsFloat = isFloat;
    }

    public bool IsReadable => Channels > 0 && BlockAlign >= Channels * BytesPerSample && BytesPerSample is >= 1 and <= 8 && SampleRate > 0;

    public static WasapiSampleLayout From(IntPtr format)
    {
        var header = Marshal.PtrToStructure<WaveFormat>(format);
        bool isFloat = header.FormatTag == WasapiConstants.FormatIeeeFloat;
        if (header.FormatTag == WasapiConstants.FormatExtensible && header.ExtraSize >= 22)
        {
            var subFormat = new byte[16];
            Marshal.Copy(format + 24, subFormat, 0, 16);
            isFloat = new Guid(subFormat) == WasapiConstants.SubtypeIeeeFloat;
        }
        return new WasapiSampleLayout(header.Channels, header.BitsPerSample / 8, header.BlockAlign, (int)header.SamplesPerSecond, isFloat);
    }

    public unsafe void Convert(byte* source, int frames, Span<StereoSample> target)
    {
        const float CentreFold = 0.7071f;
        for (int i = 0; i < frames; i++)
        {
            byte* frame = source + (long)i * BlockAlign;
            float left = Read(frame, 0);
            float right = Channels > 1 ? Read(frame, 1) : left;
            if (Channels > 2)
            {
                float centre = Read(frame, 2) * CentreFold;
                left += centre;
                right += centre;
            }
            target[i] = new StereoSample(left, right);
        }
    }

    private unsafe float Read(byte* frame, int channel)
    {
        byte* p = frame + channel * BytesPerSample;
        if (IsFloat)
            return BytesPerSample == 8 ? (float)*(double*)p : *(float*)p;
        return BytesPerSample switch
        {
            1 => (*p - 128) / 128f,
            2 => *(short*)p / 32768f,
            3 => ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f,
            4 => *(int*)p / 2147483648f,
            _ => 0f,
        };
    }
}
