// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Components.Audio;

namespace Lumora.Core.Networking.Streams;

// A stream of audio that the mixer can read as a source. The local owner writes captured samples in
// from the capture thread, the wire carries them, and every other peer's mixer reads them out on the
// audio thread. One ring buffer sits in the middle and three different threads touch it, so every
// entry point locks and none of them ever reads a sync member:
//
//   - world thread: WorldUpdate mirrors the sync members into plain fields and resizes the ring.
//   - capture thread: Write.
//   - sync thread: Encode and Decode (in the encoded subclasses).
//   - audio thread: Read, IsActive.
//
// Playback does not start the instant a sample arrives. It waits for MinimumBufferDelay worth to pile
// up first, because a network that delivers a frame late once will do it again, and a buffer with no
// slack turns every one of those into a click. Running dry drops straight back to waiting rather than
// stuttering a sample at a time. -xlinka
public abstract class AudioStream : Stream, IWorldAudioDataSource
{
    public const int DefaultBufferSize = 12000;
    public const int MinBufferSize = 2048;
    public const int MaxBufferSize = 192000;

    public const float DefaultMinimumBufferDelay = 0.05f;
    public const float MaxMinimumBufferDelay = 2f;

    public readonly Sync<int> BufferSize = new();
    public readonly Sync<float> MinimumBufferDelay = new();

    private readonly object _bufferLock = new object();

    private CircularAudioBuffer<StereoSample>? _buffer;
    private long _readPosition;
    private long _missedSamples;
    private bool _buffering = true;
    private bool _receivedData;

    // Mirrors of the sync members above, for the threads that are not allowed to touch them.
    private volatile int _capacity = DefaultBufferSize;
    private volatile float _minimumDelaySeconds = DefaultMinimumBufferDelay;
    private volatile float _lastPeak;

    public override bool HasValidData => _receivedData || IsLocal;

    // Deliberately NOT a periodic stream. A tracking stream has a value at every instant and sending
    // it on a schedule is the right shape; audio does not. Silence is the common case, and a stream
    // that ticks anyway pays a reference id and a zero every tick, per user, forever, to say nothing
    // happened. So the send point is "a whole frame is ready", which the encoded subclass answers,
    // and a quiet room costs nothing at all. -xlinka
    public override uint Period => 0;

    public override uint Phase => 0;

    public override bool IsExplicitUpdatePoint(ulong timePoint) => false;

    public int ChannelCount => 2;

    // Total samples ever written, which doubles as the write head's global index.
    public long Samples
    {
        get
        {
            lock (_bufferLock)
                return _buffer?.GlobalIndex ?? 0L;
        }
    }

    public int UnreadSamples
    {
        get
        {
            lock (_bufferLock)
                return _buffer?.AvailableSamples(_readPosition) ?? 0;
        }
    }

    // Samples the mixer asked for and the buffer could not give it. Never reset, so it reads as a
    // running count of how much of this voice was lost since it started.
    public long MissedSamples
    {
        get
        {
            lock (_bufferLock)
                return _missedSamples;
        }
    }

    public bool IsBuffering
    {
        get
        {
            lock (_bufferLock)
                return _buffering;
        }
    }

    // AUDIO THREAD. Locks, but only around a memcpy-shaped loop the other three threads also hold
    // briefly; nothing under this lock waits on anything else.
    public bool IsActive
    {
        get
        {
            if (IsDestroyed)
                return false;
            var buffer = _buffer;
            if (buffer == null)
                return false;
            return !_buffering || buffer.AvailableSamples(_readPosition) > 0;
        }
    }

    public void Read(Span<StereoSample> target, AudioSimulator simulator)
    {
        if (target.IsEmpty)
            return;

        try
        {
            lock (_bufferLock)
            {
                var buffer = _buffer;
                if (buffer == null)
                {
                    target.Clear();
                    return;
                }

                if (_readPosition < buffer.StartGlobalIndex)
                    _readPosition = buffer.StartGlobalIndex;

                if (_buffering)
                {
                    int rate = simulator != null ? simulator.SampleRate : AudioSimulator.DefaultSampleRate;
                    int wanted = (int)(_minimumDelaySeconds * rate) + target.Length;
                    if (buffer.AvailableSamples(_readPosition) < wanted)
                    {
                        target.Clear();
                        return;
                    }
                    _buffering = false;
                }

                int read = buffer.Read(target, ref _readPosition);
                if (read < target.Length)
                {
                    target.Slice(read).Clear();
                    _missedSamples += target.Length - read;
                    // Sooner or later a late frame lands; going back to waiting rebuilds the slack that
                    // stops the next one being another hole.
                    _buffering = true;
                }
            }
        }
        catch
        {
            target.Clear();
        }
    }

    // CAPTURE THREAD. The local owner's filtered microphone samples land here.
    public virtual void Write(ReadOnlySpan<StereoSample> samples) => PutSamples(samples);

    protected void PutSamples(ReadOnlySpan<StereoSample> samples)
    {
        if (samples.IsEmpty || IsDestroyed)
            return;

        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float amplitude = samples[i].AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }
        _lastPeak = peak;

        lock (_bufferLock)
        {
            var buffer = EnsureBufferLocked();
            buffer.Put(samples);
            _receivedData = true;
        }
    }

    // Loudest sample of the last batch written, whichever thread wrote it (capture for the owner, the
    // decoder for a receiver). A plain volatile float so a level meter can read it from the world thread
    // without reaching into the ring or touching a sync member. -xlinka
    public float LastPeak => _lastPeak;

    protected long WriteHead
    {
        get
        {
            lock (_bufferLock)
                return _buffer?.GlobalIndex ?? 0L;
        }
    }

    // Pulls count samples from globalPosition for the encoder. Returns how many it actually got; the
    // caller is expected to have checked there was a whole frame first.
    protected int TakeSamples(Span<StereoSample> target, ref long globalPosition)
    {
        lock (_bufferLock)
        {
            var buffer = _buffer;
            if (buffer == null)
                return 0;
            return buffer.Read(target, ref globalPosition);
        }
    }

    protected long OldestAvailableSample
    {
        get
        {
            lock (_bufferLock)
                return _buffer?.StartGlobalIndex ?? 0L;
        }
    }

    // WORLD THREAD. The engine's own stream tick (UpdateManager.UpdateStreams, from World.Update).
    // This used to be driven by UserAudioStream's component update, which meant an audio stream with no
    // component pointed at it - anything streamed into the world rather than spoken - never sized its
    // ring at all. -xlinka
    public override void Update()
    {
        base.Update();
        int wanted = System.Math.Clamp(BufferSize.Value, MinBufferSize, MaxBufferSize);

        float delay = MinimumBufferDelay.Value;
        _minimumDelaySeconds = float.IsNaN(delay) || delay < 0f
            ? 0f
            : System.Math.Min(delay, MaxMinimumBufferDelay);

        if (_capacity != wanted || _buffer == null)
        {
            lock (_bufferLock)
            {
                _capacity = wanted;
                // The old ring's contents carry into the new one, so a resize mid-sentence is not a
                // dropout, and the global index carries with them so every cursor stays valid.
                _buffer = new CircularAudioBuffer<StereoSample>(wanted, _buffer);
            }
        }

        OnWorldUpdate();
    }

    protected virtual void OnWorldUpdate()
    {
    }

    // Throws away everything buffered and starts waiting again. Used when a voice goes quiet on
    // purpose, so coming back does not play whatever was left in the ring first.
    public void ResetBuffer()
    {
        lock (_bufferLock)
        {
            _buffer?.Clear();
            _readPosition = 0L;
            _buffering = true;
        }
    }

    private CircularAudioBuffer<StereoSample> EnsureBufferLocked()
    {
        // No sync member read here: this can run on the capture thread, which only ever sees the
        // mirrored capacity.
        return _buffer ??= new CircularAudioBuffer<StereoSample>(_capacity <= 0 ? DefaultBufferSize : _capacity);
    }

    protected override void OnInit()
    {
        base.OnInit();
        BufferSize.Value = DefaultBufferSize;
        MinimumBufferDelay.Value = DefaultMinimumBufferDelay;
    }

    public override void Dispose()
    {
        lock (_bufferLock)
        {
            _buffer = null;
            _readPosition = 0L;
            _buffering = true;
        }
        base.Dispose();
    }
}
