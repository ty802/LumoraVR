// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;

namespace Lumora.Audio;

public enum WavSampleFormat
{
    Pcm16,
    Pcm24,
    Float32,
}

// Writes interleaved float PCM as a WAV file AudioDecoders reads back. Integer formats round to the nearest
// level with no dither and clamp at full scale, so a sample that already sits on the grid comes back
// bit-exact (a 16-bit file decoded and written again is the same file byte for byte, header aside). Float
// keeps every bit. Anything non-finite is written as silence.
//
// Up to two channels the plain PCM and float tags are used, which every reader knows; past two the
// extensible header carries the speaker mask that says which channel is which. Float files carry the fact
// chunk the format asks for. -xlinka
public static class WavEncoder
{
    private const ushort TagPcm = 1;
    private const ushort TagFloat = 3;
    private const ushort TagExtensible = 0xFFFE;

    // The KSDATAFORMAT_SUBTYPE GUIDs past their two-byte format tag, in file byte order.
    private static ReadOnlySpan<byte> SubtypeTail => [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    public static int BytesPerSample(WavSampleFormat format) => format switch
    {
        WavSampleFormat.Pcm16 => 2,
        WavSampleFormat.Pcm24 => 3,
        _ => 4,
    };

    public static byte[] Encode(ReadOnlySpan<float> samples, int channels, int sampleRate, WavSampleFormat format)
    {
        int frames = ClipProcessing.FrameCount(samples, channels);
        if (channels > 64)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "WAV readers stop at 64 channels");
        if (sampleRate <= 0 || sampleRate > 768000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "not a sample rate a WAV reader accepts");

        int bytesPerSample = BytesPerSample(format);
        bool isFloat = format == WavSampleFormat.Float32;
        bool extensible = channels > 2;
        long dataBytes = (long)samples.Length * bytesPerSample;
        int padByte = (int)(dataBytes & 1);
        int fmtSize = extensible ? 40 : isFloat ? 18 : 16;
        int factSize = isFloat ? 12 : 0;
        long total = 12 + 8 + fmtSize + factSize + 8 + dataBytes + padByte;
        if (total > int.MaxValue)
            throw new ArgumentException("the clip is too long for one WAV buffer", nameof(samples));

        var bytes = new byte[total];
        var span = bytes.AsSpan();
        int at = 0;

        Tag(span, ref at, "RIFF");
        U32(span, ref at, (uint)(total - 8));
        Tag(span, ref at, "WAVE");

        Tag(span, ref at, "fmt ");
        U32(span, ref at, (uint)fmtSize);
        ushort tag = isFloat ? TagFloat : TagPcm;
        U16(span, ref at, extensible ? TagExtensible : tag);
        U16(span, ref at, (ushort)channels);
        U32(span, ref at, (uint)sampleRate);
        U32(span, ref at, (uint)(sampleRate * channels * bytesPerSample));
        U16(span, ref at, (ushort)(channels * bytesPerSample));
        U16(span, ref at, (ushort)(bytesPerSample * 8));
        if (fmtSize >= 18)
            U16(span, ref at, (ushort)(extensible ? 22 : 0));
        if (extensible)
        {
            U16(span, ref at, (ushort)(bytesPerSample * 8));
            // The usual speaker order fills the mask from the bottom; past eighteen channels there are no
            // positions left to name.
            U32(span, ref at, channels <= 18 ? (uint)((1 << channels) - 1) : 0u);
            U16(span, ref at, tag);
            SubtypeTail.CopyTo(span.Slice(at));
            at += SubtypeTail.Length;
        }

        if (isFloat)
        {
            Tag(span, ref at, "fact");
            U32(span, ref at, 4);
            U32(span, ref at, (uint)frames);
        }

        Tag(span, ref at, "data");
        U32(span, ref at, (uint)dataBytes);
        var data = span.Slice(at, (int)dataBytes);
        switch (format)
        {
            case WavSampleFormat.Pcm16:
                for (int i = 0; i < samples.Length; i++)
                    BinaryPrimitives.WriteInt16LittleEndian(data.Slice(i * 2), (short)Quantize(samples[i], 32768.0, -32768, 32767));
                break;
            case WavSampleFormat.Pcm24:
                for (int i = 0; i < samples.Length; i++)
                {
                    int value = Quantize(samples[i], 8388608.0, -8388608, 8388607);
                    int o = i * 3;
                    data[o] = (byte)value;
                    data[o + 1] = (byte)(value >> 8);
                    data[o + 2] = (byte)(value >> 16);
                }
                break;
            default:
                for (int i = 0; i < samples.Length; i++)
                {
                    float value = samples[i];
                    BinaryPrimitives.WriteSingleLittleEndian(data.Slice(i * 4), float.IsFinite(value) ? value : 0f);
                }
                break;
        }
        return bytes;
    }

    // The decoder reads a level as value / scale, so value = round(sample * scale) lands every on-grid sample
    // back where it was.
    private static int Quantize(float sample, double scale, int min, int max)
    {
        if (!float.IsFinite(sample))
            return 0;
        double value = Math.Round(sample * scale);
        return value <= min ? min : value >= max ? max : (int)value;
    }

    private static void Tag(Span<byte> span, ref int at, string tag)
    {
        for (int i = 0; i < 4; i++)
            span[at + i] = (byte)tag[i];
        at += 4;
    }

    private static void U16(Span<byte> span, ref int at, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(at), value);
        at += 2;
    }

    private static void U32(Span<byte> span, ref int at, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(at), value);
        at += 4;
    }
}
