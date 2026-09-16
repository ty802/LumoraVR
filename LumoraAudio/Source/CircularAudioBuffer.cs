// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One writer, any number of readers, each reader keeping its own global position. The global index
// never wraps, so a reader that fell behind can tell exactly how much it missed instead of reading
// stale data as if it were new. Not thread-safe by itself: the owner locks around Put/Read when
// writer and reader are different threads, because the lock also has to cover the reader's
// position bookkeeping. -xlinka
public sealed class CircularAudioBuffer<S> where S : unmanaged, IAudioSample<S>
{
    private readonly S[] _data;
    private long _globalIndex;
    private int _head;

    public CircularAudioBuffer(int length, CircularAudioBuffer<S>? initializeWith = null)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        _data = new S[length];
        if (initializeWith != null)
        {
            // Carry over what the old buffer still held so a resize mid-stream is not a dropout.
            int carry = Math.Min(length, initializeWith.AvailableSamples(initializeWith.StartGlobalIndex));
            long from = initializeWith._globalIndex - carry;
            initializeWith.Read(_data.AsSpan(0, carry), ref from);
            _head = carry % length;
            _globalIndex = initializeWith._globalIndex;
        }
    }

    public int Length => _data.Length;

    // Index of the next sample to be written; samples [GlobalIndex - Length, GlobalIndex) are readable.
    public long GlobalIndex => _globalIndex;

    public long StartGlobalIndex => Math.Max(0L, _globalIndex - _data.Length);

    // How many samples a reader at lastIndex can still read before it catches the writer.
    public int AvailableSamples(long lastIndex)
    {
        long available = _globalIndex - Math.Max(lastIndex, StartGlobalIndex);
        return (int)Math.Clamp(available, 0, _data.Length);
    }

    public void Put(ReadOnlySpan<S> source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            _data[_head] = source[i];
            _head = _head + 1 == _data.Length ? 0 : _head + 1;
        }
        _globalIndex += source.Length;
    }

    // Reads from globalPosition forward at rate 1, advancing it. Returns how many samples were
    // available; the caller fills the rest with silence and treats the shortfall as an underrun.
    public int Read(Span<S> target, ref long globalPosition)
    {
        if (globalPosition < StartGlobalIndex)
            globalPosition = StartGlobalIndex;
        int available = AvailableSamples(globalPosition);
        int count = Math.Min(available, target.Length);
        int readIndex = IndexOf(globalPosition);
        for (int i = 0; i < count; i++)
        {
            target[i] = _data[readIndex];
            readIndex = readIndex + 1 == _data.Length ? 0 : readIndex + 1;
        }
        globalPosition += count;
        return count;
    }

    // Sample at an absolute global position, for resampling readers that keep their own cursor.
    // The caller guarantees StartGlobalIndex <= globalPosition < GlobalIndex.
    public S this[long globalPosition] => _data[IndexOf(globalPosition)];

    // Writes source resampled by rate (source samples advanced per buffer sample written). position
    // is the fractional cursor into source carried between calls, lastSample the sample before it.
    // Linear interpolation; consecutive calls interpolate across the seam. Mixer-side; the video and
    // voice writers of phase 1 push at the buffer's own rate and use Put. -xlinka
    public void Put(ReadOnlySpan<S> source, ref double position, ref S lastSample, double rate)
    {
        if (source.Length == 0) return;
        if (!(rate > 0.0) || double.IsInfinity(rate)) rate = 1.0;
        // position below -1 would ask for a sample older than the one carry we keep.
        if (!(position >= -1.0)) position = -1.0;

        int last = source.Length - 1;
        while (position <= last)
        {
            int index = (int)Math.Floor(position);
            float fraction = (float)(position - index);
            S a = index < 0 ? lastSample : source[index];
            // index == last only happens at fraction 0, so b is never actually weighted there.
            S b = index < last ? source[index + 1] : a;
            _data[_head] = a.LerpTo(b, fraction);
            _head = _head + 1 == _data.Length ? 0 : _head + 1;
            _globalIndex++;
            position += rate;
        }

        lastSample = source[last];
        // Rebase the cursor onto the next call's span; it lands in (-1, rate - 1].
        position -= source.Length;
    }

    // Reads at a rate (buffer samples advanced per output sample) with linear interpolation. position
    // is the fractional cursor relative to globalPosition, lastSample the one before it. Returns
    // output samples produced; fewer than target.Length means the buffer ran dry.
    public int Read(Span<S> target, ref long globalPosition, double rate, ref double position, ref S lastSample)
    {
        if (target.Length == 0) return 0;
        if (!(rate > 0.0) || double.IsInfinity(rate)) rate = 1.0;

        if (!(position >= 0.0) || position >= 1.0)
        {
            double whole = Math.Floor(position);
            if (double.IsNaN(whole) || double.IsInfinity(whole))
            {
                position = 0.0;
            }
            else
            {
                globalPosition += (long)whole;
                position -= whole;
            }
        }

        if (globalPosition < StartGlobalIndex)
        {
            // Fell behind the writer: resync to the oldest sample still held rather than reading
            // whatever overwrote it. The caller hears a jump, not garbage.
            globalPosition = StartGlobalIndex;
            position = 0.0;
        }

        int produced = 0;
        while (produced < target.Length)
        {
            // Both ends of the interpolation have to be written already, so the newest sample in
            // the buffer is always held back one call. That is one sample of latency, not a drop.
            if (globalPosition + 1 >= _globalIndex) break;
            S a = _data[IndexOf(globalPosition)];
            S b = _data[IndexOf(globalPosition + 1)];
            target[produced++] = a.LerpTo(b, (float)position);
            position += rate;
            if (position >= 1.0)
            {
                double whole = Math.Floor(position);
                globalPosition += (long)whole;
                position -= whole;
            }
        }

        if (produced > 0 && globalPosition >= StartGlobalIndex && globalPosition < _globalIndex)
            lastSample = _data[IndexOf(globalPosition)];
        return produced;
    }

    public void Clear()
    {
        Array.Clear(_data, 0, _data.Length);
        _head = 0;
        _globalIndex = 0;
    }

    private int IndexOf(long globalPosition)
    {
        long behind = _globalIndex - globalPosition;
        int index = (int)((_head - behind) % _data.Length);
        return index < 0 ? index + _data.Length : index;
    }
}
