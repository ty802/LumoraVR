// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

/// <summary>
/// Compact synchronized array for value types and primitives.
/// Stores values in a flat T[] buffer — no per-element heap allocation.
/// Supports full and sparse-delta network encoding via SyncCoder.
/// Use this instead of SyncFieldList&lt;T&gt; for primitives: 25× less memory.
/// </summary>
public class SyncArray<T> : ConflictingSyncElement, IEnumerable<T>
{
    private const int DefaultCapacity = 4;
    private const byte ModeFullSnapshot = 0;
    private const byte ModeSparseValues = 1;

    private T[] _items = Array.Empty<T>();
    private int _count;

    // Delta tracking: structure change (add/remove/insert/clear) forces full snapshot in delta.
    // Value-only changes use sparse encoding with per-index dirty tracking.
    private bool _structureChanged;
    private HashSet<int>? _dirtyIndices;

    public int Count => _count;

    public event Action<SyncArray<T>, int, int>? ElementsAdded;
    public event Action<SyncArray<T>, int, int>? ElementsRemoved;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        set
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!BeginModification()) return;
            _items[index] = value;
            if (!_structureChanged)
                (_dirtyIndices ??= new HashSet<int>()).Add(index);
            InvalidateSyncElement();
            EndModification();
        }
    }

    public void Add(T item)
    {
        if (!BeginModification()) return;
        EnsureCapacity(_count + 1);
        int idx = _count;
        _items[_count++] = item;
        _structureChanged = true;
        _dirtyIndices?.Clear();
        InvalidateSyncElement();
        BlockModification();
        ElementsAdded?.Invoke(this, idx, 1);
        UnblockModification();
        EndModification();
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (!BeginModification()) return;
        int startIdx = _count;
        foreach (var item in items)
        {
            EnsureCapacity(_count + 1);
            _items[_count++] = item;
        }
        if (_count > startIdx)
        {
            _structureChanged = true;
            _dirtyIndices?.Clear();
            InvalidateSyncElement();
            BlockModification();
            ElementsAdded?.Invoke(this, startIdx, _count - startIdx);
            UnblockModification();
        }
        EndModification();
    }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!BeginModification()) return;
        EnsureCapacity(_count + 1);
        if (index < _count)
            Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = item;
        _count++;
        _structureChanged = true;
        _dirtyIndices?.Clear();
        InvalidateSyncElement();
        BlockModification();
        ElementsAdded?.Invoke(this, index, 1);
        UnblockModification();
        EndModification();
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!BeginModification()) return;
        _count--;
        if (index < _count)
            Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = default!;
        _structureChanged = true;
        _dirtyIndices?.Clear();
        InvalidateSyncElement();
        BlockModification();
        ElementsRemoved?.Invoke(this, index, 1);
        UnblockModification();
        EndModification();
    }

    public bool Remove(T item)
    {
        int idx = IndexOf(item);
        if (idx < 0) return false;
        RemoveAt(idx);
        return true;
    }

    public void Clear()
    {
        if (_count == 0) return;
        if (!BeginModification()) return;
        int old = _count;
        Array.Clear(_items, 0, _count);
        _count = 0;
        _structureChanged = true;
        _dirtyIndices?.Clear();
        InvalidateSyncElement();
        BlockModification();
        ElementsRemoved?.Invoke(this, 0, old);
        UnblockModification();
        EndModification();
    }

    public int IndexOf(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _count; i++)
        {
            if (comparer.Equals(_items[i], item)) return i;
        }
        return -1;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    private void EnsureCapacity(int needed)
    {
        if (needed <= _items.Length) return;
        int newCap = System.Math.Max(DefaultCapacity, System.Math.Max(needed, _items.Length * 2));
        var newArr = new T[newCap];
        if (_count > 0) Array.Copy(_items, newArr, _count);
        _items = newArr;
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write7BitEncoded((ulong)_count);
        for (int i = 0; i < _count; i++)
            SyncCoder.Encode(writer, _items[i]);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        int count = (int)reader.Read7BitEncoded();
        if (_items.Length < count)
            _items = new T[System.Math.Max(DefaultCapacity, count)];
        else
            Array.Clear(_items, 0, _count);
        for (int i = 0; i < count; i++)
            _items[i] = SyncCoder.Decode<T>(reader);
        int old = _count;
        _count = count;
        BlockModification();
        if (count > 0) ElementsAdded?.Invoke(this, 0, count);
        else if (old > 0) ElementsRemoved?.Invoke(this, 0, old);
        UnblockModification();
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        // Structure changed (add/remove/insert/clear): send full snapshot inside delta slot
        if (_structureChanged || _dirtyIndices == null || _dirtyIndices.Count == 0)
        {
            writer.Write(ModeFullSnapshot);
            InternalEncodeFull(writer, outboundMessage);
            return;
        }
        // Only value updates at known indices: sparse encoding
        writer.Write(ModeSparseValues);
        writer.Write7BitEncoded((ulong)_dirtyIndices.Count);
        foreach (int idx in _dirtyIndices)
        {
            writer.Write7BitEncoded((ulong)idx);
            SyncCoder.Encode(writer, _items[idx]);
        }
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        byte mode = reader.ReadByte();
        if (mode == ModeFullSnapshot)
        {
            InternalDecodeFull(reader, inboundMessage);
            return;
        }
        // Sparse: apply value updates at individual indices
        int changedCount = (int)reader.Read7BitEncoded();
        for (int i = 0; i < changedCount; i++)
        {
            int idx = (int)reader.Read7BitEncoded();
            T value = SyncCoder.Decode<T>(reader);
            if ((uint)idx < (uint)_count)
                _items[idx] = value;
        }
    }

    protected override void InternalClearDirty()
    {
        _structureChanged = false;
        _dirtyIndices?.Clear();
    }

<<<<<<< Updated upstream
    public override object? GetValueAsObject() => null;

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override void Dispose()
    {
        ElementsAdded = null;
        ElementsRemoved = null;
        _dirtyIndices?.Clear();
        _dirtyIndices = null;
        _items = Array.Empty<T>();
        _count = 0;
        base.Dispose();
    }
}
=======
    public override object GetValueAsObject() => _items;
}
>>>>>>> Stashed changes
