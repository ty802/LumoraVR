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
// Playback does not start the instant a sample arrives. It waits for the target: how late frames have
// been landing over the last two seconds (95th percentile) plus one mixer block, 20 to 120 ms, never
// under MinimumBufferDelay. A fixed small buffer holes on the first bad second and a fixed big one is
// latency forever. Running dry conceals a frame or two first, because a late frame is usually a few
// milliseconds away, and only then goes back to waiting, for the target and nothing more. A ring that
// sits well above the target plays a few percent fast until it is back. -xlinka
public abstract class AudioStream : Stream, IWorldAudioDataSource
{
    public const int DefaultBufferSize = 12000;
    public const int MinBufferSize = 2048;
    public const int MaxBufferSize = 192000;

    public const float DefaultMinimumBufferDelay = 0.02f;
    public const float MaxMinimumBufferDelay = 2f;

    public const int MaxUnderrunConcealFrames = 2;

    // Seconds.
    private const double MinTargetDelay = 0.02;
    private const double MaxTargetDelay = 0.12;
    private const float InitialTargetDelay = 0.04f;
    private const double TargetMargin = 0.01;
    private const double JitterWindow = 2.0;
    private const int JitterHistory = 256;
    private const int MinJitterSamples = 10;
    // Two messages whose sender clock moved this much further than the audio they carry had a pause
    // between them, not a slow network: silence is never sent.
    private const double SenderPause = 0.05;
    private const double SameBurst = 0.002;
    private const double CatchUpExcess = 0.03;
    private const double CatchUpHold = 0.2;
    // Speed-up per second of excess: 2% at 40 ms over the target, 5% from 100 ms.
    private const double CatchUpGain = 0.5;
    private const double MinCatchUpSpeed = 0.02;
    private const double MaxCatchUpSpeed = 0.05;

    public readonly Sync<int> BufferSize = new();
    public readonly Sync<float> MinimumBufferDelay = new();

    private readonly object _bufferLock = new object();

    private CircularAudioBuffer<StereoSample>? _buffer;
    private long _readPosition;
    private long _missedSamples;
    private bool _buffering = true;
    private bool _receivedData;

    // Audio thread, under the buffer lock.
    private bool _catchingUp;
    private int _aboveTargetSamples;
    private double _readFraction;
    private StereoSample _readCarry;
    private long _playbackIndex;

    // Mirrors of the sync members above, for the threads that are not allowed to touch them.
    private volatile int _capacity = DefaultBufferSize;
    private volatile float _minimumDelaySeconds = DefaultMinimumBufferDelay;
    private volatile float _lastPeak;
    private volatile int _sampleRate = AudioSimulator.DefaultSampleRate;
    private volatile float _targetDelaySeconds = InitialTargetDelay;

    // Decode thread only.
    private double[]? _arrivalTimes;
    private double[]? _arrivalDelays;
    private int[]? _arrivalRuns;
    private bool[]? _arrivalCounted;
    private double[]? _jitterScratch;
    private int _arrivalHead;
    private int _arrivalCount;
    private int _arrivalRun;
    private double _lastSenderLead;
    private double _lastArrival;

    // Receiver side. Raised right after decoded or concealed samples land in the ring; firstIndex is the
    // ring's global index of samples[0]. Usually on the sync thread, but concealing a late frame runs it
    // on the mixer thread. Calls never overlap and indices only grow. The span is scratch: copy what is
    // kept, and never block in here. -xlinka
    public delegate void DecodedAudioHandler(AudioStream stream, ReadOnlySpan<StereoSample> samples, long firstIndex);

    public event DecodedAudioHandler? SamplesDecoded;

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

    // Ring global index the mixer reads next. Any thread.
    public long PlaybackIndex => System.Threading.Interlocked.Read(ref _playbackIndex);

    // Rate the ring plays at, mirrored from the mixer; ring indices are samples at this rate.
    public int SampleRate => _sampleRate;

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
            int rate = simulator != null ? simulator.SampleRate : AudioSimulator.DefaultSampleRate;
            _sampleRate = rate;

            int filled = ReadBuffered(target, rate);
            // Outside the buffer lock: concealing raises SamplesDecoded.
            for (int i = 0; filled >= 0 && filled < target.Length && i < MaxUnderrunConcealFrames; i++)
            {
                if (ConcealUnderrun() <= 0)
                    break;
                filled = ReadMore(target, filled);
            }

            if (filled >= 0 && filled < target.Length)
                EndUnderrun(target, filled);
        }
        catch
        {
            target.Clear();
        }
    }

    // Samples read, or -1 while waiting for the target (target cleared).
    private int ReadBuffered(Span<StereoSample> target, int rate)
    {
        lock (_bufferLock)
        {
            var buffer = _buffer;
            if (buffer == null)
            {
                target.Clear();
                return -1;
            }

            if (_readPosition < buffer.StartGlobalIndex)
                _readPosition = buffer.StartGlobalIndex;

            int available = buffer.AvailableSamples(_readPosition);
            int wanted = System.Math.Max((int)(_targetDelaySeconds * rate), (int)(_minimumDelaySeconds * rate));
            if (_buffering)
            {
                if (available < wanted)
                {
                    target.Clear();
                    PublishPlaybackIndex();
                    return -1;
                }
                _buffering = false;
                _catchingUp = false;
                _aboveTargetSamples = 0;
            }

            double speed = CatchUpSpeed(available, wanted, target.Length, rate);
            int read = speed > 0.0
                ? buffer.Read(target, ref _readPosition, 1.0 + speed, ref _readFraction, ref _readCarry)
                : buffer.Read(target, ref _readPosition);

            PublishPlaybackIndex();
            return read;
        }
    }

    // A ring that has sat well above the target is latency nobody asked for. Playing it a few percent
    // fast moves the pitch by the same few percent, which nobody notices; a jump, everybody does. -xlinka
    private double CatchUpSpeed(int available, int wanted, int block, int rate)
    {
        double excess = (available - wanted) / (double)rate;
        if (_catchingUp)
        {
            if (excess > 0.0)
                return System.Math.Clamp(excess * CatchUpGain, MinCatchUpSpeed, MaxCatchUpSpeed);
            _catchingUp = false;
            _aboveTargetSamples = 0;
            _readFraction = 0.0;
            return 0.0;
        }

        if (excess <= CatchUpExcess)
        {
            _aboveTargetSamples = 0;
            return 0.0;
        }

        _aboveTargetSamples += block;
        if (_aboveTargetSamples < CatchUpHold * rate)
            return 0.0;

        _catchingUp = true;
        _readFraction = 0.0;
        return System.Math.Clamp(excess * CatchUpGain, MinCatchUpSpeed, MaxCatchUpSpeed);
    }

    private int ReadMore(Span<StereoSample> target, int filled)
    {
        lock (_bufferLock)
        {
            var buffer = _buffer;
            if (buffer == null)
                return filled;
            filled += buffer.Read(target.Slice(filled), ref _readPosition);
            PublishPlaybackIndex();
            return filled;
        }
    }

    private void EndUnderrun(Span<StereoSample> target, int filled)
    {
        target.Slice(filled).Clear();
        lock (_bufferLock)
        {
            _missedSamples += target.Length - filled;
            _buffering = true;
            _catchingUp = false;
            _aboveTargetSamples = 0;
            _readFraction = 0.0;
        }
    }

    private void PublishPlaybackIndex() => System.Threading.Interlocked.Exchange(ref _playbackIndex, _readPosition);

    // AUDIO THREAD, no locks held. A receiver that can make up a late frame puts it in the ring and
    // returns how many samples it put; it must not wait for anything.
    protected virtual int ConcealUnderrun() => 0;

    // CAPTURE THREAD. The local owner's filtered microphone samples land here.
    public virtual void Write(ReadOnlySpan<StereoSample> samples) => PutSamples(samples);

    protected void PutSamples(ReadOnlySpan<StereoSample> samples) => PutAt(samples);

    // Decoded or concealed receiver audio. The caller holds whatever keeps these calls from overlapping.
    protected void PutDecoded(ReadOnlySpan<StereoSample> samples)
    {
        long first = PutAt(samples);
        if (first < 0)
            return;
        try
        {
            SamplesDecoded?.Invoke(this, samples, first);
        }
        catch
        {
            // A listener that throws loses its own copy, not everybody's audio.
        }
    }

    // Ring global index of samples[0], or -1 when nothing was written.
    private long PutAt(ReadOnlySpan<StereoSample> samples)
    {
        if (samples.IsEmpty || IsDestroyed)
            return -1;

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
            long first = buffer.GlobalIndex;
            buffer.Put(samples);
            _receivedData = true;
            return first;
        }
    }

    // DECODE THREAD, once per received message that carried audio. mediaStart is where its first sample
    // sits in the sender's timeline (lost frames counted, nothing made up here counted); senderTime is the
    // sender's clock when it went out. Wall clock on purpose: this measures the network, not the audio.
    //
    // Lateness is arrival minus media time, relative to the earliest message of its run. A run ends where
    // the sender's clock jumped past its audio, which is a pause (or a sender hitch, which is not this
    // network's fault either), so a pause never reads as one long late frame. -xlinka
    protected void NoteArrival(double senderTime, long mediaStart)
    {
        double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        double media = mediaStart / (double)_sampleRate;
        double senderLead = senderTime - media;

        var times = _arrivalTimes ??= new double[JitterHistory];
        var delays = _arrivalDelays ??= new double[JitterHistory];
        var runs = _arrivalRuns ??= new int[JitterHistory];
        var counted = _arrivalCounted ??= new bool[JitterHistory];
        var scratch = _jitterScratch ??= new double[JitterHistory];

        if (_arrivalCount > 0 && senderLead - _lastSenderLead > SenderPause)
            _arrivalRun++;
        _lastSenderLead = senderLead;

        // Messages decoded back to back arrived as one burst, after a stall here or on the way: one late
        // event, not several. The rest of the burst still sets how early a message can be.
        bool burstTail = _arrivalCount > 0 && now - _lastArrival < SameBurst;
        _lastArrival = now;

        times[_arrivalHead] = now;
        delays[_arrivalHead] = now - media;
        runs[_arrivalHead] = _arrivalRun;
        counted[_arrivalHead] = !burstTail;
        _arrivalHead = _arrivalHead + 1 == JitterHistory ? 0 : _arrivalHead + 1;
        if (_arrivalCount < JitterHistory)
            _arrivalCount++;
        if (burstTail)
            return;

        double since = now - JitterWindow;
        int oldest = _arrivalHead - _arrivalCount;
        if (oldest < 0)
            oldest += JitterHistory;

        int n = 0;
        int runStart = 0;
        int run = int.MinValue;
        double earliest = 0.0;
        for (int k = 0; k < _arrivalCount; k++)
        {
            int i = oldest + k;
            if (i >= JitterHistory)
                i -= JitterHistory;
            if (times[i] < since)
                continue;
            if (runs[i] != run)
            {
                for (int j = runStart; j < n; j++)
                    scratch[j] -= earliest;
                run = runs[i];
                runStart = n;
                earliest = delays[i];
            }
            if (counted[i])
                scratch[n++] = delays[i];
            if (delays[i] < earliest)
                earliest = delays[i];
        }
        for (int j = runStart; j < n; j++)
            scratch[j] -= earliest;

        // Too little recent evidence: a target set high by a stall long ago falls back to the start value.
        if (n < MinJitterSamples)
        {
            if (_targetDelaySeconds > InitialTargetDelay)
                _targetDelaySeconds = InitialTargetDelay;
            return;
        }

        Array.Sort(scratch, 0, n);
        double late = scratch[(int)(0.95 * (n - 1) + 0.5)];
        _targetDelaySeconds = (float)System.Math.Clamp(late + TargetMargin, MinTargetDelay, MaxTargetDelay);
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
            _catchingUp = false;
            _aboveTargetSamples = 0;
            _readFraction = 0.0;
            PublishPlaybackIndex();
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
            PublishPlaybackIndex();
        }
        SamplesDecoded = null;
        base.Dispose();
    }
}
