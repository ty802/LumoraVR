// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;

namespace Lumora.Core.Networking.Streams;

// The Opus stream. Voice uses it mono, and that is the default, because a microphone is one capsule
// and a head is one point in the room: the position a listener hears it from is the avatar's, not
// anything the capture could have carried, so a second channel would be paying for a copy of the first.
//
// Channels is a member rather than a constructor argument because a stream is reconstructed from its
// type name on the receiving side and has nowhere to put a constructor argument. It replicates, so both
// ends build the same codec; changing it rebuilds both.
//
// Only the owner holds an encoder and only a receiver holds a decoder, so a peer never allocates a
// codec for a direction it does not use. Both are built lazily on the sync thread the first time
// there is something to do, from mirrors of the settings rather than the sync members. -xlinka
public class OpusStream : EncodedAudioStream
{
    public const int DefaultBitRate = 25000;

    public readonly Sync<int> BitRate = new();
    public readonly Sync<OpusFrameDuration> FrameDuration = new();
    public readonly Sync<int> Channels = new();

    // What this stream carries. Replicated, because the DECODER does not need it but every peer
    // rebuilds the encoder from the same members and the inspector should read the same thing
    // everywhere. Voice is the default: a user's microphone is by far the common case, and a stream
    // that never says otherwise should behave exactly as it always has. -xlinka
    public readonly Sync<OpusContent> Content = new();

    private OpusVoiceEncoder? _encoder;
    private OpusVoiceDecoder? _decoder;

    private float[] _pcm = Array.Empty<float>();

    // Mirrors, so neither the capture thread nor a codec build reads a sync member.
    private volatile int _bitRate = DefaultBitRate;
    private OpusFrameDuration _frameDuration = OpusFrameDuration.Ms20;
    private volatile int _channels = 1;
    private OpusContent _content = OpusContent.Voice;

    protected override int MaxPacketBytes => OpusVoiceEncoder.MaxPacketBytes;

    protected override void OnInit()
    {
        base.OnInit();
        BitRate.Value = DefaultBitRate;
        FrameDuration.Value = OpusFrameDuration.Ms20;
        Channels.Value = 1;
        Content.Value = OpusContent.Voice;
    }

    protected override void OnWorldUpdate()
    {
        base.OnWorldUpdate();

        _bitRate = BitRate.Value;
        var duration = FrameDuration.Value;
        if (duration != _frameDuration)
        {
            _frameDuration = duration;
            // The frame size is baked into the encoder, so a duration change is a rebuild. The decoder
            // does not care: the packet carries its own duration and only concealment needs the number.
            // Under the codec lock because the encode worker may be inside the encoder right now.
            lock (CodecLock)
            {
                _encoder?.Dispose();
                _encoder = null;
            }
        }

        int channels = Channels.Value == 2 ? 2 : 1;
        if (channels != _channels)
        {
            _channels = channels;
            // Channel count is baked into BOTH codecs, so this one is a full reset on either side.
            lock (CodecLock)
                ResetCodecs();
        }

        var content = Content.Value;
        if (content != _content)
        {
            _content = content;
            // Application type and FEC are constructor-time on the encoder; the decoder is agnostic.
            lock (CodecLock)
            {
                _encoder?.Dispose();
                _encoder = null;
            }
        }

        FrameSizeSamples = _frameDuration.FrameSize();

        lock (CodecLock)
        {
            var encoder = _encoder;
            if (encoder != null)
                encoder.Bitrate = _bitRate;
        }
    }

    protected override int EncodeFrame(ReadOnlySpan<StereoSample> frame, Span<byte> packet)
    {
        var encoder = EnsureEncoder();
        if (encoder == null)
            return 0;

        int frameSize = encoder.FrameSize;
        if (frame.Length < frameSize)
            return 0;

        int channels = encoder.Channels;
        EnsurePcm(frameSize * channels);
        var pcm = _pcm.AsSpan(0, frameSize * channels);
        if (channels == 2)
        {
            for (int i = 0; i < frameSize; i++)
            {
                pcm[i * 2] = frame[i].left;
                pcm[i * 2 + 1] = frame[i].right;
            }
        }
        else
        {
            for (int i = 0; i < frameSize; i++)
                pcm[i] = frame[i].ToMono().value;
        }

        return encoder.Encode(pcm, packet);
    }

    protected override int DecodeFrame(ReadOnlySpan<byte> packet, Span<StereoSample> samples)
    {
        var decoder = EnsureDecoder();
        if (decoder == null)
            return 0;

        EnsurePcm(OpusVoiceDecoder.MaxFrameSize * decoder.Channels);
        int produced = decoder.Decode(packet, _pcm);
        return Spread(produced, samples, decoder.Channels);
    }

    protected override int ConcealFrame(int frameSize, Span<StereoSample> samples)
    {
        var decoder = EnsureDecoder();
        if (decoder == null || frameSize <= 0)
            return 0;

        int channels = decoder.Channels;
        EnsurePcm(frameSize * channels);
        int produced = decoder.DecodeLost(frameSize, _pcm.AsSpan(0, frameSize * channels));
        return Spread(produced, samples, channels);
    }

    // Mono out of the codec goes to both ears: the mixer positions it from the avatar's head, so what
    // goes in is the same signal twice and not a stereo image. A stereo stream de-interleaves instead,
    // because there the two channels ARE the image.
    private int Spread(int produced, Span<StereoSample> samples, int channels)
    {
        if (produced <= 0)
            return 0;
        int count = System.Math.Min(produced, samples.Length);
        if (channels == 2)
        {
            for (int i = 0; i < count; i++)
                samples[i] = new StereoSample(_pcm[i * 2], _pcm[i * 2 + 1]);
        }
        else
        {
            for (int i = 0; i < count; i++)
                samples[i] = new StereoSample(_pcm[i]);
        }
        return count;
    }

    private OpusVoiceEncoder? EnsureEncoder()
    {
        if (_encoder != null)
            return _encoder;
        if (!IsLocal)
            return null;

        try
        {
            _encoder = new OpusVoiceEncoder(_channels, _bitRate, _frameDuration, Engine.IsMobilePlatform, _content);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"OpusStream: could not build a voice encoder: {ex.Message}");
            _encoder = null;
        }
        return _encoder;
    }

    private OpusVoiceDecoder? EnsureDecoder()
    {
        if (_decoder != null)
            return _decoder;

        try
        {
            _decoder = new OpusVoiceDecoder(_channels);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"OpusStream: could not build a voice decoder: {ex.Message}");
            _decoder = null;
        }
        return _decoder;
    }

    private void EnsurePcm(int samples)
    {
        if (_pcm.Length < samples)
            _pcm = new float[samples];
    }

    protected override void ResetCodecs()
    {
        var encoder = _encoder;
        _encoder = null;
        encoder?.Dispose();

        var decoder = _decoder;
        _decoder = null;
        decoder?.Dispose();
    }
}
