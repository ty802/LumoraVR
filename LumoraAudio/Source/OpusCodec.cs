// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Concentus;
using Concentus.Enums;

namespace Lumora.Audio;

// Voice codec seams over the managed Opus package. One encoder per local voice stream, one decoder
// per remote one. Frames are fixed-size at 48 kHz; the stream above decides the frame duration.
// Nothing outside this file touches the package, so a native codec can replace it without the stream
// layer noticing. -xlinka
public enum OpusFrameDuration
{
    Ms10,
    Ms20,
    Ms40,
    Ms60,
}

public static class OpusFrameDurations
{
    public static float Seconds(this OpusFrameDuration duration) => duration switch
    {
        OpusFrameDuration.Ms10 => 0.01f,
        OpusFrameDuration.Ms40 => 0.04f,
        OpusFrameDuration.Ms60 => 0.06f,
        _ => 0.02f,
    };

    // Samples per channel in one frame at 48 kHz.
    public static int FrameSize(this OpusFrameDuration duration) => duration switch
    {
        OpusFrameDuration.Ms10 => 480,
        OpusFrameDuration.Ms40 => 1920,
        OpusFrameDuration.Ms60 => 2880,
        _ => 960,
    };
}

public sealed class OpusVoiceEncoder : IDisposable
{
    public const int SampleRate = 48000;

    // A 60 ms stereo frame at the ceiling bitrate still lands far inside this, and the encoder is
    // handed the real span length as its own cap anyway.
    public const int MaxPacketBytes = 4000;

    private const int MinBitrate = 6000;
    private const int MaxBitrate = 510000;

    private IOpusEncoder? _encoder;
    private int _bitrate;

    public OpusVoiceEncoder(int channels, int bitrate, OpusFrameDuration frameDuration, bool lowComplexity)
    {
        if (channels != 1 && channels != 2)
            throw new ArgumentOutOfRangeException(nameof(channels), "Opus encodes 1 or 2 channels.");

        Channels = channels;
        FrameDuration = frameDuration;

        var encoder = OpusCodecFactory.CreateEncoder(SampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP, null);
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        // The managed encoder is the most expensive thing on the capture path, and complexity is the
        // one knob that trades CPU for quality directly. Standalone hardware cannot pay for 10.
        encoder.Complexity = lowComplexity ? 2 : 10;
        _encoder = encoder;

        Bitrate = bitrate;
    }

    public int Channels { get; }

    public OpusFrameDuration FrameDuration { get; }

    public int FrameSize => FrameDuration.FrameSize();

    public int Bitrate
    {
        get => _bitrate;
        set
        {
            int clamped = value < MinBitrate ? MinBitrate : (value > MaxBitrate ? MaxBitrate : value);
            if (clamped == _bitrate)
                return;
            _bitrate = clamped;
            var encoder = _encoder;
            if (encoder != null)
                encoder.Bitrate = clamped;
        }
    }

    // pcm holds exactly FrameSize * Channels interleaved floats; returns the packet length written.
    public int Encode(ReadOnlySpan<float> pcm, Span<byte> packet)
    {
        var encoder = _encoder;
        if (encoder == null || packet.IsEmpty)
            return 0;

        int frameSize = FrameSize;
        if (pcm.Length < frameSize * Channels)
            return 0;

        return encoder.Encode(pcm, frameSize, packet, packet.Length);
    }

    public void Dispose()
    {
        var encoder = _encoder;
        _encoder = null;
        if (encoder is IDisposable disposable)
            disposable.Dispose();
    }
}

public sealed class OpusVoiceDecoder : IDisposable
{
    public const int SampleRate = 48000;

    // 120 ms at 48 kHz, the longest thing a single packet can carry.
    public const int MaxFrameSize = 5760;

    private IOpusDecoder? _decoder;

    public OpusVoiceDecoder(int channels)
    {
        if (channels != 1 && channels != 2)
            throw new ArgumentOutOfRangeException(nameof(channels), "Opus decodes 1 or 2 channels.");

        Channels = channels;
        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, channels, null);
    }

    public int Channels { get; }

    // Returns frames decoded (per channel); pcm must hold at least 5760 * Channels floats (60 ms).
    public int Decode(ReadOnlySpan<byte> packet, Span<float> pcm)
    {
        var decoder = _decoder;
        if (decoder == null || pcm.IsEmpty)
            return 0;

        int room = pcm.Length / Channels;
        if (room > MaxFrameSize)
            room = MaxFrameSize;
        if (room <= 0)
            return 0;

        return decoder.Decode(packet, pcm, room, false);
    }

    // Packet loss concealment for one missing frame of the given size. The codec wants the exact
    // duration of what went missing, not whatever space happens to be free, or it comes back out of
    // step for the packet that does arrive.
    public int DecodeLost(int frameSize, Span<float> pcm)
    {
        var decoder = _decoder;
        if (decoder == null || frameSize <= 0 || pcm.Length < frameSize * Channels)
            return 0;

        return decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, frameSize, false);
    }

    public void Dispose()
    {
        var decoder = _decoder;
        _decoder = null;
        if (decoder is IDisposable disposable)
            disposable.Dispose();
    }
}
