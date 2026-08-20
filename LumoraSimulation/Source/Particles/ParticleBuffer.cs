// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Simulation.Particles;

// One entry of a compaction plan: the live particle at Src moves down into the hole at Dst.
public readonly struct ParticleMove
{
    public readonly int Dst;
    public readonly int Src;

    public ParticleMove(int dst, int src)
    {
        Dst = dst;
        Src = src;
    }
}

// Growable struct-of-arrays column. Every per-particle attribute in the simulation - the shared ones
// the core owns and the private ones individual modules keep - lives in one of these, and they all
// stay index-aligned because deaths are applied to every column from the SAME precomputed move plan
// (see ParticleSimulation). Get that wrong and a particle's velocity ends up on some other particle's
// position, which looks exactly like a physics bug and is not one. -xlinka
public sealed class ParticleBuffer<T> where T : unmanaged
{
    private const int DefaultCapacity = 128;

    private T[] _data;

    public int Count { get; private set; }

    public int Capacity => _data.Length;

    public ParticleBuffer(int capacity = DefaultCapacity)
        => _data = new T[System.Math.Max(capacity, 1)];

    // The raw backing array. Valid entries are [0, Count).
    public T[] Array => _data;

    public Span<T> AsSpan() => _data.AsSpan();

    public Span<T> Live => _data.AsSpan(0, Count);

    public Span<T> Slice(int offset, int count) => _data.AsSpan(offset, count);

    // Appends room for the new entries and returns just that slice.
    public Span<T> IncreaseCount(int newParticles)
    {
        int required = Count + newParticles;
        if (required > _data.Length)
            Grow(System.Math.Max(_data.Length * 2, required));
        Count = required;
        return _data.AsSpan(Count - newParticles, newParticles);
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _data.Length)
            Grow(capacity);
    }

    private void Grow(int capacity)
    {
        var grown = new T[capacity];
        System.Array.Copy(_data, grown, Count);
        _data = grown;
    }

    // Applies a death compaction plan produced by the owning simulation.
    public void ApplyCompaction(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        for (int i = 0; i < moves.Length; i++)
            _data[moves[i].Dst] = _data[moves[i].Src];
        Count = newCount;
    }

    // Drops the tail without touching the surviving entries.
    public void TrimParticles(int newCount)
    {
        if (newCount < 0 || newCount > Count)
            throw new ArgumentOutOfRangeException(nameof(newCount));
        Count = newCount;
    }

    public void Clear() => Count = 0;
}
