// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// Point history for every live trail in one system, as a fixed-stride slab plus a free list.
//
// Every trail gets the same MaxPoints-sized block, addressed as a ring, and a slot is claimed and
// released by popping and pushing one integer. Nothing moves, nothing is defragmented, and the whole
// store stops allocating the moment every slot has been used once - which is the point, because a
// trail system churns slots at the emission rate and a per-trail allocator would be handing the GC a
// steady stream of garbage for as long as the effect is on screen.
//
// The cost of the fixed stride is that a short trail still reserves a full block. That is a bounded,
// up-front MaxTrails * MaxPoints and it is worth it: the alternative is a packing allocator that has
// to relocate live trails mid-frame when a block runs out, which is where the alternatives spend
// their time and their allocations. -xlinka
internal sealed class TrailPointStore
{
    private float3[] _positions = System.Array.Empty<float3>();
    private colorHDR[] _colors = System.Array.Empty<colorHDR>();
    private float[] _widths = System.Array.Empty<float>();

    // Simulation time the point was laid, not an age counter: ageing by subtraction from the clock is
    // one op for the whole trail instead of one per point per frame, and it cannot drift.
    private float[] _times = System.Array.Empty<float>();

    private int[] _head = System.Array.Empty<int>();
    private int[] _count = System.Array.Empty<int>();
    private int[] _free = System.Array.Empty<int>();
    private int _freeCount;

    public int SlotCapacity { get; private set; }

    public int PointsPerSlot { get; private set; }

    public int FreeSlots => _freeCount;

    public int UsedSlots => SlotCapacity - _freeCount;

    // Resizes the slab. Everything currently stored is dropped, so callers must release their slots
    // first; reshaping while trails are live has no sane answer and pretending otherwise hides bugs.
    public void Configure(int slotCapacity, int pointsPerSlot)
    {
        slotCapacity = System.Math.Max(1, slotCapacity);
        pointsPerSlot = System.Math.Max(2, pointsPerSlot);
        if (slotCapacity == SlotCapacity && pointsPerSlot == PointsPerSlot)
            return;

        SlotCapacity = slotCapacity;
        PointsPerSlot = pointsPerSlot;
        int total = slotCapacity * pointsPerSlot;

        _positions = new float3[total];
        _colors = new colorHDR[total];
        _widths = new float[total];
        _times = new float[total];
        _head = new int[slotCapacity];
        _count = new int[slotCapacity];
        _free = new int[slotCapacity];
        Clear();
    }

    public void Clear()
    {
        _freeCount = SlotCapacity;
        for (int i = 0; i < SlotCapacity; i++)
        {
            // Handed out from the end, so a fresh store issues slot 0 first and the debug view reads
            // in the order trails were created.
            _free[i] = SlotCapacity - 1 - i;
            _head[i] = 0;
            _count[i] = 0;
        }
    }

    public bool TryAllocate(out int slot)
    {
        if (_freeCount == 0)
        {
            slot = -1;
            return false;
        }
        slot = _free[--_freeCount];
        _head[slot] = 0;
        _count[slot] = 0;
        return true;
    }

    public void Release(int slot)
    {
        _count[slot] = 0;
        _head[slot] = 0;
        _free[_freeCount++] = slot;
    }

    public int CountOf(int slot) => _count[slot];

    public bool IsFull(int slot) => _count[slot] >= PointsPerSlot;

    // Raw index of the point `age` places back from the head. 0 is the newest point, CountOf-1 the
    // oldest. Anything outside that range is the caller's bug, not a wrap.
    public int RawIndex(int slot, int age)
    {
        int offset = _head[slot] - age;
        if (offset < 0)
            offset += PointsPerSlot;
        return slot * PointsPerSlot + offset;
    }

    // Pushes a new head point. When the ring is full this overwrites the oldest point, which is the
    // MaxPoints cap doing its job.
    public int Push(int slot, in float3 position, in colorHDR color, float width, float time)
    {
        int head = _head[slot];
        if (_count[slot] == 0)
        {
            _count[slot] = 1;
        }
        else
        {
            head++;
            if (head == PointsPerSlot)
                head = 0;
            _head[slot] = head;
            if (_count[slot] < PointsPerSlot)
                _count[slot]++;
        }

        int raw = slot * PointsPerSlot + _head[slot];
        _positions[raw] = position;
        _colors[raw] = color;
        _widths[raw] = width;
        _times[raw] = time;
        return raw;
    }

    // Drops the oldest point. The head does not move, so the newest point keeps its index.
    public void DropOldest(int slot)
    {
        if (_count[slot] > 0)
            _count[slot]--;
    }

    public ref float3 Position(int raw) => ref _positions[raw];

    public ref colorHDR Color(int raw) => ref _colors[raw];

    public ref float Width(int raw) => ref _widths[raw];

    public ref float Time(int raw) => ref _times[raw];
}
