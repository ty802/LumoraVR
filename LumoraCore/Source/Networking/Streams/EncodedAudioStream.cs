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
    // whole backlog would just push the latency it created onto everybody listening. Not lower:
    // capture arrives once per frame, so a 10 fps frame already brings five, and what does get
    // through is played a few percent fast on the other end until it is gone.
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

    // ENCODING DOES NOT BELONG ON THE SYNC THREAD.
    //
    // It used to run inside Encode, which is called by the sync loop while it is building the tick's
    // outbound message - the same thread, and the same moment, that encodes the delta for the entire
    // datamodel. A stereo frame at full complexity is real arithmetic, and paying for it there means
    // every voice in the room adds its codec time to the interval between one tick being sent and the
    // next, which arrives at the other end as jitter on everything, not just on audio.
    //
    // So frames are encoded on a pool thread as they finish, and the sync thread's job shrinks to
    // copying ready bytes into the writer. One encode is ever in flight; the flag is the whole
    // scheduling policy, because a second one could only produce packets out of order. -xlinka
    private readonly System.Collections.Concurrent.ConcurrentQueue<EncodedPacket> _ready = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _bufferPool = new();
    private int _encodeInFlight;
    private volatile int _readyCount;
    private volatile bool _encodeStopped;

    // The codec is one object and cannot be inside two threads at once. This guards every use of it
    // and every rebuild of it, which is the pool thread encoding against the world thread swapping a
    // bitrate or a channel count underneath.
    protected readonly object CodecLock = new();

    private readonly struct EncodedPacket
    {
        public readonly int Index;
        public readonly byte[] Data;
        public readonly int Length;

        public EncodedPacket(int index, byte[] data, int length)
        {
            Index = index;
            Data = data;
            Length = length;
        }
    }

    // Receiver side.
    private int _expectedIndex;
    private bool _hasExpectedIndex;
    // Sender timeline in samples: decoded audio plus lost frames, never concealment made up here.
    private long _mediaSamples;
    // Frames the mixer concealed since the last decoded one. Under CodecLock.
    private int _concealedAhead;
    private volatile bool _receiving;

    private byte[] _packetScratch = Array.Empty<byte>();
    private StereoSample[] _frameScratch = Array.Empty<StereoSample>();
    // Owned by the encode worker alone, which is why it is not the one above.
    private StereoSample[] _encodeFrameScratch = Array.Empty<StereoSample>();
    private StereoSample[] _decodeScratch = Array.Empty<StereoSample>();
    // The mixer's, sized on the sync thread so the audio thread never sizes it.
    private StereoSample[] _concealScratch = Array.Empty<StereoSample>();

    // Mirror of MinimumVolume, because the gate is applied on the capture thread.
    private volatile float _minimumVolume = DefaultMinimumVolume;

    // Quiet blocks still go out for this long after the last loud one, so a dip inside a word is not
    // cut out of it. Seconds.
    private const float SilenceHangover = 0.1f;

    // Capture thread. Starts past the hangover: an open microphone sends nothing until something is said.
    private int _quietSamples = int.MaxValue / 2;

    // Samples per channel in one codec frame. Plain, not a sync read: both the capture thread and the
    // sync thread need it. The subclass mirrors it from its own settings on the world thread.
    protected int FrameSizeSamples { get; set; } = 960;

    protected abstract int MaxPacketBytes { get; }

    // All four are called under CodecLock: encode on the pool thread, decode on the sync thread,
    // concealment on the sync thread or the mixer's.
    protected abstract int EncodeFrame(ReadOnlySpan<StereoSample> frame, Span<byte> packet);
    protected abstract int DecodeFrame(ReadOnlySpan<byte> packet, Span<StereoSample> samples);
    protected abstract int ConcealFrame(int frameSize, Span<StereoSample> samples);
    protected abstract void ResetCodecs();

    // CAPTURE THREAD. Silence is dropped before it reaches the ring, so it is never encoded and never
    // sent. It is decided by level: once nothing has reached MinimumVolume for the hangover, the
    // part-built frame is finished from the head of the block and the rest goes nowhere. Finishing it
    // matters, or its first half would sit in the ring until the next word and arrive as a stale fragment
    // of the last one. Waiting for a write to land exactly on a frame boundary instead kept silence going
    // out for seconds whenever capture blocks were not frame multiples. -xlinka
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

        if (peak >= _minimumVolume)
        {
            _quietSamples = 0;
        }
        else
        {
            bool hangover = _quietSamples < SilenceHangover * SampleRate;
            _quietSamples = (int)System.Math.Min(int.MaxValue / 2, (long)_quietSamples + samples.Length);
            if (!hangover)
            {
                int owed = SamplesToFinishFrame;
                if (owed <= 0)
                    return;
                if (owed < samples.Length)
                    samples = samples.Slice(0, owed);
            }
        }

        PutSamples(samples);

        // Captured on this thread, encoded on another, sent on a third. The trigger is here because
        // this is the moment new audio exists and nothing else has to poll for it.
        TriggerEncode();
    }

    // Kick a pool thread if one is not already working. Cheap enough to call on every captured block:
    // the common case is a single interlocked compare that finds an encode already running.
    private void TriggerEncode()
    {
        if (!IsLocal || !HasWholeFrame)
            return;
        if (System.Threading.Interlocked.CompareExchange(ref _encodeInFlight, 1, 0) != 0)
            return;

        System.Threading.ThreadPool.UnsafeQueueUserWorkItem(static state => ((EncodedAudioStream)state!).RunEncode(), this);
    }

    private bool HasWholeFrame
    {
        get
        {
            int frameSize = FrameSizeSamples;
            if (frameSize <= 0 || !_encodePositionValid)
                return false;
            return WriteHead - _encodePosition >= frameSize;
        }
    }

    // POOL THREAD.
    private void RunEncode()
    {
        try
        {
            int guard = 0;
            while (IsLocal && !_encodeStopped && HasWholeFrame && guard++ < MaxFramesPerEncode * 4)
            {
                // Anything already waiting to be sent is a backlog, and adding to it only makes the
                // voice arrive later. Stop and let the sync thread catch up.
                if (_readyCount >= MaxFramesPerEncode)
                    break;

                int frameSize = FrameSizeSamples;
                if (frameSize <= 0)
                    break;

                long oldest = OldestAvailableSample;
                if (_encodePosition < oldest)
                    _encodePosition = oldest;

                // Behind by more than anybody wants to hear: skip forward rather than ship a backlog.
                long pendingFrames = (WriteHead - _encodePosition) / frameSize;
                if (pendingFrames > MaxFramesPerEncode)
                    _encodePosition += (pendingFrames - MaxFramesPerEncode) * frameSize;

                EnsureCapacity(ref _encodeFrameScratch, frameSize);
                var frame = _encodeFrameScratch.AsSpan(0, frameSize);
                int taken = TakeSamples(frame, ref _encodePosition);
                if (taken < frameSize)
                    frame.Slice(taken).Clear();

                var buffer = RentBuffer();
                int length = 0;
                try
                {
                    lock (CodecLock)
                        length = EncodeFrame(frame, buffer);
                }
                catch (Exception ex)
                {
                    Logging.Logger.Warn($"EncodedAudioStream: encode failed, dropping a frame: {ex.Message}");
                    length = 0;
                }

                if (length < 0 || length > buffer.Length)
                    length = 0;

                // Queued even when it failed: the receiver sees the index, sees no payload and counts
                // one lost frame, rather than losing sync with everything after it.
                _ready.Enqueue(new EncodedPacket(_packetIndex++, buffer, length));
                System.Threading.Interlocked.Increment(ref _readyCount);
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"EncodedAudioStream: the encode worker threw: {ex}");
        }
        finally
        {
            System.Threading.Volatile.Write(ref _encodeInFlight, 0);
            // A block that arrived while this was finishing would have seen the flag set and gone
            // home, so check once more rather than wait for the next one.
            if (HasWholeFrame && _readyCount < MaxFramesPerEncode)
                TriggerEncode();
        }
    }

    private byte[] RentBuffer()
    {
        if (_bufferPool.TryDequeue(out var buffer) && buffer.Length >= MaxPacketBytes)
            return buffer;
        return new byte[MaxPacketBytes];
    }

    private void ReturnBuffer(byte[] buffer)
    {
        // A handful is plenty: the queue is bounded at MaxFramesPerEncode.
        if (_bufferPool.Count < MaxFramesPerEncode * 2)
            _bufferPool.Enqueue(buffer);
    }

    // Samples the part-built frame still needs; 0 when the ring ends on a frame boundary.
    private int SamplesToFinishFrame
    {
        get
        {
            int frameSize = FrameSizeSamples;
            if (frameSize <= 0 || !_encodePositionValid)
                return 0;
            long pending = WriteHead - _encodePosition;
            if (pending <= 0)
                return 0;
            int part = (int)(pending % frameSize);
            return part == 0 ? 0 : frameSize - part;
        }
    }

    // Drops whatever has been captured but not yet encoded and restarts the codec state. Called when a
    // voice is muted, so unmuting does not open with the tail of what was being said when it went off.
    public void FlushEncoder()
    {
        _encodePosition = WriteHead;
        _encodePositionValid = true;
        _quietSamples = int.MaxValue / 2;
        // Whatever was encoded and not yet sent is the tail of what was being said when the voice
        // went off, and sending it after the fact is the bug this exists to prevent.
        while (_ready.TryDequeue(out var stale))
        {
            System.Threading.Interlocked.Decrement(ref _readyCount);
            ReturnBuffer(stale.Data);
        }
        lock (CodecLock)
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

        // A packet already encoded is a packet to send. Captured-but-not-yet-encoded audio counts
        // too, but only to get a worker started: the send itself carries whatever is ready by then.
        if (_readyCount > 0)
            return true;

        if (head - _encodePosition >= frameSize)
        {
            TriggerEncode();
            return false;
        }

        return false;
    }

    // SYNC THREAD. Nothing but a copy: the packets were built on a pool thread while this one was
    // busy with the rest of the tick.
    public override void Encode(BinaryWriter writer)
    {
        if (!_encodePositionValid)
        {
            _encodePosition = WriteHead;
            _encodePositionValid = true;
        }

        int count = System.Math.Min(_readyCount, MaxFramesPerEncode);
        if (count <= 0)
        {
            writer.Write7BitEncodedInt(0);
            // Audio arrived but nothing has encoded it yet: the worker may have finished between the
            // send check and here, so make sure one is on its way rather than wait for a capture
            // block that might not come for a while.
            TriggerEncode();
            return;
        }

        writer.Write7BitEncodedInt(count);

        for (int i = 0; i < count; i++)
        {
            if (!_ready.TryDequeue(out var packet))
                break;
            System.Threading.Interlocked.Decrement(ref _readyCount);

            writer.Write7BitEncodedInt(packet.Index);
            writer.Write7BitEncodedInt(packet.Length);
            if (packet.Length > 0)
                writer.Write(packet.Data, 0, packet.Length);

            ReturnBuffer(packet.Data);
        }

        // More audio may have landed while this ran.
        TriggerEncode();
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
        long mediaStart = -1;

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

            // The mixer conceals under the same lock, and decoder state is one timeline.
            lock (CodecLock)
            {
                EnsureCapacity(ref _concealScratch, OpusVoiceDecoder.MaxFrameSize);
                ConcealGap(index, frameSize);
                _expectedIndex = index + 1;
                _hasExpectedIndex = true;

                if (read == 0)
                {
                    _mediaSamples += frameSize;
                    continue;
                }

                if (mediaStart < 0)
                    mediaStart = _mediaSamples;

                int produced = 0;
                try
                {
                    produced = System.Math.Min(DecodeFrame(_packetScratch.AsSpan(0, read), _decodeScratch), _decodeScratch.Length);
                }
                catch (Exception ex)
                {
                    Logging.Logger.Warn($"EncodedAudioStream: decode failed, dropping a packet: {ex.Message}");
                    ResetCodecs();
                }

                if (produced > 0)
                {
                    _mediaSamples += produced;
                    _concealedAhead = 0;
                    PutDecoded(_decodeScratch.AsSpan(0, produced));
                }
            }
        }

        if (mediaStart >= 0)
            NoteArrival(message != null ? message.StreamTime : double.NaN, mediaStart);
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

        // Lost frames are the sender's time whoever fills them in.
        _mediaSamples += (long)gap * frameSize;

        if (gap > MaxConcealedFrames)
        {
            ResetCodecs();
            return;
        }

        // The mixer may already have covered some of them while it waited.
        for (int i = _concealedAhead; i < gap; i++)
        {
            int produced;
            try
            {
                produced = System.Math.Min(ConcealFrame(frameSize, _decodeScratch), _decodeScratch.Length);
            }
            catch
            {
                // Concealment that will not run is one more frame of silence, not a failed decode.
                return;
            }
            if (produced > 0)
                PutDecoded(_decodeScratch.AsSpan(0, produced));
        }
    }

    // AUDIO THREAD. Never waits for the codec: if the sync thread has it, the frame being waited for is
    // being decoded right now. A sender only stops after a stretch under MinimumVolume, so running dry
    // right after a quiet frame is the end of a sentence, not a late frame, and is left alone (2x for
    // codec noise).
    protected override int ConcealUnderrun()
    {
        if (!_receiving || LastPeak < _minimumVolume * 2f || !System.Threading.Monitor.TryEnter(CodecLock))
            return 0;

        try
        {
            var scratch = _concealScratch;
            int frameSize = FrameSizeSamples;
            if (_concealedAhead >= MaxUnderrunConcealFrames || frameSize <= 0 || scratch.Length < frameSize)
                return 0;

            int produced = System.Math.Min(ConcealFrame(frameSize, scratch), scratch.Length);
            if (produced <= 0)
                return 0;

            _concealedAhead++;
            PutDecoded(scratch.AsSpan(0, produced));
            return produced;
        }
        catch
        {
            return 0;
        }
        finally
        {
            System.Threading.Monitor.Exit(CodecLock);
        }
    }

    protected override void OnWorldUpdate()
    {
        base.OnWorldUpdate();

        float minimum = MinimumVolume.Value;
        _minimumVolume = float.IsNaN(minimum) || minimum < 0f ? 0f : System.Math.Min(minimum, 1f);
        _receiving = !IsLocal;
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
        // The encode worker holds a reference to this stream, so it is told to stop before the codec
        // under it is torn down. Under the lock for the same reason: it may be inside the encoder now.
        _encodeStopped = true;
        while (_ready.TryDequeue(out _))
            System.Threading.Interlocked.Decrement(ref _readyCount);
        lock (CodecLock)
            ResetCodecs();
        base.Dispose();
    }
}
