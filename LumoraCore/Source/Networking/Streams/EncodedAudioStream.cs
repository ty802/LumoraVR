// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Audio;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Networking.Streams;

// An audio stream that goes over the wire as compressed frames instead of raw samples.
//
// The transport does not run at the codec's rate and never will: the sync loop ticks at the world's
// tick rate and calls Encode once per tick per stream, while a codec frame is a fixed slice of time.
// So Encode does not encode "the current frame", it drains whole frames out of the capture ring and
// writes every one that is ready, count first. At a 70 Hz tick and 20 ms frames that is usually one
// packet, sometimes two, occasionally none, and the receiver does not care which because it is
// reassembling a timeline, not a per-tick value.
//
// Every transmitted packet carries an index. Indices count packets SENT, not frames of wall clock, so
// going quiet does not look like packet loss: the gap in indices is what loss looks like, and silence
// leaves no gap because silence is never sent. A gap of up to MaxConcealedFrames is filled by the
// codec's own concealment before the packet that did arrive is decoded; anything larger is treated as
// a new start rather than a minute of invented audio. -xlinka
public abstract class EncodedAudioStream : AudioStream
{
    // Longer than this and concealment is making up audio nobody said. At 20 ms frames this is a third
    // of a second, which is already generous for a hole to paper over.
    public const int MaxConcealedFrames = 16;

    // A tick that produced more than this is a stall somewhere upstream, not speech. Encoding the
    // whole backlog would just push the latency it created onto everybody listening.
    public const int MaxFramesPerEncode = 8;

    // What a single message is allowed to claim it carries, so a hostile or corrupt frame cannot make
    // the decoder walk a buffer it does not have.
    public const int MaxPacketsPerMessage = 32;

    public const float DefaultMinimumVolume = 0.005f;

    public readonly Sync<float> MinimumVolume = new();

    // Owner side.
    private long _encodePosition;
    private int _packetIndex;
    private bool _encodePositionValid;

    // Receiver side.
    private int _expectedIndex;
    private bool _hasExpectedIndex;

    private byte[] _packetScratch = Array.Empty<byte>();
    private StereoSample[] _frameScratch = Array.Empty<StereoSample>();
    private StereoSample[] _decodeScratch = Array.Empty<StereoSample>();

    // Mirror of MinimumVolume, because the gate is applied on the capture thread.
    private volatile float _minimumVolume = DefaultMinimumVolume;

    // Samples per channel in one codec frame. Plain, not a sync read: both the capture thread and the
    // sync thread need it. The subclass mirrors it from its own settings on the world thread.
    protected int FrameSizeSamples { get; set; } = 960;

    protected abstract int MaxPacketBytes { get; }

    // All four run on the sync thread only.
    protected abstract int EncodeFrame(ReadOnlySpan<StereoSample> frame, Span<byte> packet);
    protected abstract int DecodeFrame(ReadOnlySpan<byte> packet, Span<StereoSample> samples);
    protected abstract int ConcealFrame(int frameSize, Span<StereoSample> samples);
    protected abstract void ResetCodecs();

    // CAPTURE THREAD. Silence is dropped before it reaches the ring, so it is never encoded and never
    // sent. A part-built frame is the exception: dropping the tail of a frame already half captured
    // would leave it stuck in the ring until the next word, arriving as a stale fragment of the last
    // one, so the remainder is let through to finish it.
    public override void Write(ReadOnlySpan<StereoSample> samples)
    {
        if (samples.IsEmpty)
            return;

        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float amplitude = samples[i].AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }

        if (peak < _minimumVolume && !HasPartialFrame)
            return;

        PutSamples(samples);
    }

    private bool HasPartialFrame
    {
        get
        {
            int frameSize = FrameSizeSamples;
            if (frameSize <= 0 || !_encodePositionValid)
                return false;
            long pending = WriteHead - _encodePosition;
            return pending > 0 && pending % frameSize != 0;
        }
    }

    // Drops whatever has been captured but not yet encoded and restarts the codec state. Called when a
    // voice is muted, so unmuting does not open with the tail of what was being said when it went off.
    public void FlushEncoder()
    {
        _encodePosition = WriteHead;
        _encodePositionValid = true;
        ResetCodecs();
    }

    // SYNC THREAD. The send point, asked once per tick per stream: is there a whole frame waiting.
    public override bool IsExplicitUpdatePoint(ulong timePoint)
    {
        if (!IsLocal)
            return false;

        int frameSize = FrameSizeSamples;
        if (frameSize <= 0)
            return false;

        long head = WriteHead;
        if (!_encodePositionValid)
        {
            _encodePosition = head;
            _encodePositionValid = true;
            return false;
        }

        return head - _encodePosition >= frameSize;
    }

    // SYNC THREAD
    public override void Encode(BinaryWriter writer)
    {
        int frameSize = FrameSizeSamples;
        long head = WriteHead;

        if (!_encodePositionValid)
        {
            _encodePosition = head;
            _encodePositionValid = true;
        }

        long oldest = OldestAvailableSample;
        if (_encodePosition < oldest)
            _encodePosition = oldest;

        int frames = frameSize > 0 ? (int)((head - _encodePosition) / frameSize) : 0;
        if (frames <= 0)
        {
            writer.Write7BitEncodedInt(0);
            return;
        }

        if (frames > MaxFramesPerEncode)
        {
            // Behind by more than anybody wants to hear: skip forward instead of shipping a backlog.
            _encodePosition += (long)(frames - MaxFramesPerEncode) * frameSize;
            frames = MaxFramesPerEncode;
        }

        EnsureCapacity(ref _frameScratch, frameSize);
        EnsureCapacity(ref _packetScratch, MaxPacketBytes);

        writer.Write7BitEncodedInt(frames);

        for (int i = 0; i < frames; i++)
        {
            var frame = _frameScratch.AsSpan(0, frameSize);
            int taken = TakeSamples(frame, ref _encodePosition);
            if (taken < frameSize)
                frame.Slice(taken).Clear();

            int length = 0;
            try
            {
                length = EncodeFrame(frame, _packetScratch);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"EncodedAudioStream: encode failed, dropping a frame: {ex.Message}");
            }

            if (length < 0 || length > _packetScratch.Length)
                length = 0;

            writer.Write7BitEncodedInt(_packetIndex);
            writer.Write7BitEncodedInt(length);
            if (length > 0)
                writer.Write(_packetScratch, 0, length);

            // Counted even for a frame that failed to encode: the receiver sees the index, sees no
            // payload, and treats it as one lost frame rather than losing sync with everything after.
            _packetIndex++;
        }
    }

    // SYNC THREAD. Must consume exactly what Encode wrote: several streams share one message and the
    // reader walks straight on to the next one.
    public override void Decode(BinaryReader reader, StreamMessage message)
    {
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > MaxPacketsPerMessage)
            throw new InvalidDataException($"Audio stream declared {count} packets (cap {MaxPacketsPerMessage}).");

        if (count == 0)
            return;

        int frameSize = FrameSizeSamples;
        EnsureCapacity(ref _decodeScratch, OpusVoiceDecoder.MaxFrameSize);
        EnsureCapacity(ref _packetScratch, MaxPacketBytes);

        for (int i = 0; i < count; i++)
        {
            int index = reader.Read7BitEncodedInt();
            int length = reader.Read7BitEncodedInt();
            if (length < 0 || length > MaxPacketBytes)
                throw new InvalidDataException($"Audio packet declared {length} bytes (cap {MaxPacketBytes}).");

            int read = length > 0 ? reader.Read(_packetScratch, 0, length) : 0;
            if (read < length)
                throw new InvalidDataException("Audio packet ended early.");

            // The local owner's own stream is written by capture, not by the wire. Bytes still have to
            // be consumed so the rest of the group decodes, but they are not played back.
            if (IsLocal)
                continue;

            ConcealGap(index, frameSize);
            _expectedIndex = index + 1;
            _hasExpectedIndex = true;

            if (read == 0)
                continue;

            try
            {
                int produced = DecodeFrame(_packetScratch.AsSpan(0, read), _decodeScratch);
                if (produced > 0)
                    PutSamples(_decodeScratch.AsSpan(0, System.Math.Min(produced, _decodeScratch.Length)));
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn($"EncodedAudioStream: decode failed, dropping a packet: {ex.Message}");
                ResetCodecs();
            }
        }
    }

    private void ConcealGap(int index, int frameSize)
    {
        if (!_hasExpectedIndex || frameSize <= 0)
            return;

        int gap = index - _expectedIndex;
        if (gap <= 0)
        {
            if (gap < 0)
            {
                // Out of order or a restarted sender. Nothing sensible to conceal toward, so pick the
                // new index up as the truth and let the codec resettle on the next packet.
                ResetCodecs();
            }
            return;
        }

        if (gap > MaxConcealedFrames)
        {
            ResetCodecs();
            return;
        }

        for (int i = 0; i < gap; i++)
        {
            try
            {
                int produced = ConcealFrame(frameSize, _decodeScratch);
                if (produced > 0)
                    PutSamples(_decodeScratch.AsSpan(0, System.Math.Min(produced, _decodeScratch.Length)));
            }
            catch
            {
                // Concealment that will not run is one more frame of silence, not a failed decode.
                return;
            }
        }
    }

    protected override void OnWorldUpdate()
    {
        base.OnWorldUpdate();

        float minimum = MinimumVolume.Value;
        _minimumVolume = float.IsNaN(minimum) || minimum < 0f ? 0f : System.Math.Min(minimum, 1f);
    }

    protected override void OnInit()
    {
        base.OnInit();
        MinimumVolume.Value = DefaultMinimumVolume;
    }

    protected static void EnsureCapacity(ref StereoSample[] buffer, int length)
    {
        if (buffer.Length < length)
            buffer = new StereoSample[length];
    }

    protected static void EnsureCapacity(ref byte[] buffer, int length)
    {
        if (buffer.Length < length)
            buffer = new byte[length];
    }

    public override void Dispose()
    {
        ResetCodecs();
        base.Dispose();
    }
}
