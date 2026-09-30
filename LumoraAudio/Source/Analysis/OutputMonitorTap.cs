// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Audio;

// Every block an output voice plays, in order, for a reader on another thread.
//
// A voice's monitor only ever holds its last block, and a reader polling it from the world thread sees
// whichever block happened to be there: at 60 frames a second against 94 renders a second it misses a third
// of them, and at 144 it reads some twice. Nothing that keeps time (loudness, onsets, tempo) survives that.
// So the copy is taken where the blocks are made, at the top of every render, when the monitor holds exactly
// the block the render before produced, and goes into a ring the reader drains at its own pace.
//
// The top of a render is before any voice is pulled, so the voice's peak still describes the previous
// render: zero means it made nothing that render (its source was stopped, it was culled, or it was silent),
// and that render is written as silence rather than as the stale block still in the monitor. A voice whose
// world was in the background for that render made nothing either; whether it was is remembered from the
// render before, since a world coming back to the foreground flips its space on between renders while the
// monitor still holds a block from before it left. The copy costs one block of memcpy per render on the
// audio thread and never waits; a reader that falls a whole ring behind loses the oldest blocks and is told
// how many samples went. One writer, one reader. -xlinka
public sealed class OutputMonitorTap : IDisposable
{
    public const int DefaultCapacity = 16384;

    private const int MonitorLength = 1024;

    private readonly AudioSimulator _simulator;
    private readonly Action<AudioSimulator> _onRender;
    private readonly StereoSample[] _ring;
    private readonly StereoSample[] _block = new StereoSample[MonitorLength];
    private readonly int _mask;
    private AudioOutputVoice? _voice;
    private AudioOutputVoice? _lastVoice;
    private bool _wasActive;
    private long _written;
    private long _read;
    private long _dropped;
    private bool _disposed;

    public OutputMonitorTap(AudioSimulator simulator, int capacity = DefaultCapacity)
    {
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        int size = 1024;
        while (size < capacity)
            size <<= 1;
        _ring = new StereoSample[size];
        _mask = size - 1;
        _onRender = OnRender;
        _simulator.RenderStarted += _onRender;
    }

    public AudioSimulator Simulator => _simulator;

    public int Capacity => _ring.Length;

    // Written from the reader's thread; the audio thread takes it up at the next render.
    public AudioOutputVoice? Voice
    {
        get => Volatile.Read(ref _voice);
        set => Volatile.Write(ref _voice, value);
    }

    // Samples the reader never saw because it fell a whole ring behind.
    public long Dropped => Interlocked.Read(ref _dropped);

    // Samples written and not yet read.
    public int Pending => (int)Math.Min(_ring.Length, Volatile.Read(ref _written) - _read);

    // Reader's thread: moves up to destination.Length samples out in the order they were played, and returns
    // how many.
    public int Read(Span<StereoSample> destination)
    {
        long written = Volatile.Read(ref _written);
        long start = _read;
        if (written - start > _ring.Length)
        {
            Interlocked.Add(ref _dropped, written - _ring.Length - start);
            start = written - _ring.Length;
        }
        int count = (int)Math.Min(written - start, destination.Length);
        for (int i = 0; i < count; i++)
            destination[i] = _ring[(int)((start + i) & _mask)];

        // Whatever the writer lapped while this copied may be half old and half new: dropped, not handed on.
        long safe = Volatile.Read(ref _written) - _ring.Length;
        if (safe > start)
        {
            int torn = (int)Math.Min(count, safe - start);
            Interlocked.Add(ref _dropped, torn);
            destination.Slice(torn, count - torn).CopyTo(destination);
            count -= torn;
            start += torn;
        }
        _read = start + count;
        return count;
    }

    // Reader's thread: forgets everything not yet read.
    public void Skip() => _read = Volatile.Read(ref _written);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _simulator.RenderStarted -= _onRender;
        Volatile.Write(ref _voice, null);
    }

    private void OnRender(AudioSimulator simulator)
    {
        try
        {
            var voice = Volatile.Read(ref _voice);
            if (voice == null)
            {
                _lastVoice = null;
                return;
            }
            bool active = voice.Space.IsActive;
            bool wasActive = ReferenceEquals(voice, _lastVoice) ? _wasActive : active;
            _lastVoice = voice;
            _wasActive = active;
            int frame = Math.Min(Math.Max(1, simulator.FrameSize), MonitorLength);
            if (!wasActive || voice.LastPeakAmplitude <= 0f)
            {
                Silence(frame);
                return;
            }
            int count = voice.CopyMonitor(_block);
            if (count <= 0)
            {
                Silence(frame);
                return;
            }
            Put(_block.AsSpan(0, count));
        }
        catch
        {
            // A handler that throws stops every handler after it; this one never does.
        }
    }

    private void Silence(int count)
    {
        _block.AsSpan(0, count).Clear();
        Put(_block.AsSpan(0, count));
    }

    private void Put(ReadOnlySpan<StereoSample> samples)
    {
        long at = _written;
        int start = (int)(at & _mask);
        int first = Math.Min(samples.Length, _ring.Length - start);
        samples.Slice(0, first).CopyTo(_ring.AsSpan(start));
        if (first < samples.Length)
            samples.Slice(first).CopyTo(_ring);
        Volatile.Write(ref _written, at + samples.Length);
    }
}
