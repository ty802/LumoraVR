// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Analysed windows in transit from the thread the audio arrives on (the voice chain, or the sync
// thread decoding a stream) to the world thread, each stamped with the sample index it is centred on.
// Fixed size, allocation free, and the lock is held for a copy of fifteen floats. Full drops the
// oldest: a window nobody took in time is a window for audio that has already been heard. -xlinka
public sealed class VisemeFrames
{
    // 2.7 s of windows at 48 kHz: past the longest buffer delay a stream allows, plus the device.
    public const int DefaultCapacity = 512;

    private const int Stride = VisemeAnalysis.Count;

    private readonly object _lock = new();
    private readonly long[] _stamps;
    private readonly float[] _weights;
    private int _head;
    private int _count;

    public VisemeFrames(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            capacity = 1;
        _stamps = new long[capacity];
        _weights = new float[capacity * Stride];
    }

    public void Push(long stamp, ReadOnlySpan<float> weights)
    {
        lock (_lock)
        {
            int capacity = _stamps.Length;
            if (_count == capacity)
            {
                _head = (_head + 1) % capacity;
                _count--;
            }
            int slot = (_head + _count) % capacity;
            _stamps[slot] = stamp;
            var into = _weights.AsSpan(slot * Stride, Stride);
            int n = System.Math.Min(weights.Length, Stride);
            weights.Slice(0, n).CopyTo(into);
            if (n < Stride)
                into.Slice(n).Clear();
            _count++;
        }
    }

    // Takes everything waiting and copies out the newest. False when nothing was waiting.
    public bool TakeLatest(Span<float> weights) => TakeUpTo(long.MaxValue, weights, out _);

    // Takes every window centred at or before position and copies out the last of them. False, with
    // weights untouched, when none was.
    public bool TakeUpTo(long position, Span<float> weights, out long stamp)
    {
        lock (_lock)
        {
            int capacity = _stamps.Length;
            int last = -1;
            while (_count > 0 && _stamps[_head] <= position)
            {
                last = _head;
                _head = (_head + 1) % capacity;
                _count--;
            }

            if (last < 0)
            {
                stamp = 0L;
                return false;
            }

            stamp = _stamps[last];
            int n = System.Math.Min(weights.Length, Stride);
            _weights.AsSpan(last * Stride, n).CopyTo(weights);
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
