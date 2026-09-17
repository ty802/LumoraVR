// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

public class SyncFieldList<T> : SyncElementList<Sync<T>>, IEnumerable<T>, IEnumerable
{
    public event Action<SyncFieldList<T>>? OnChanged;

    public SyncFieldList()
    {
        // Wire up element events to fire OnChanged
        ElementsAdded += (list, idx, count) => OnChanged?.Invoke(this);
        ElementsRemoved += (list, idx, count) => OnChanged?.Invoke(this);
    }

    public SyncFieldList(Component owner) : this()
    {
        if (owner?.World != null)
        {
            Initialize(owner.World, owner);
        }
    }

    public new struct Enumerator : IEnumerator<T>, IDisposable, IEnumerator
    {
        private SyncElementList<Sync<T>>.Enumerator _baseEnumerator;

        public T Current => _baseEnumerator.Current.Value;

        object IEnumerator.Current => Current!;

        internal Enumerator(SyncElementList<Sync<T>>.Enumerator baseEnumerator)
        {
            _baseEnumerator = baseEnumerator;
        }

        public void Dispose()
        {
            _baseEnumerator.Dispose();
        }

        public bool MoveNext()
        {
            return _baseEnumerator.MoveNext();
        }

        public void Reset()
        {
            _baseEnumerator.Reset();
        }
    }

    public new T this[int index]
    {
        get => GetElement(index).Value;
        set => GetElement(index).Value = value;
    }

    public void Add(T value)
    {
        // A refused add (a config-locked list) hands back nothing rather than throwing, so the
        // convenience wrapper has to read that as "did not add" instead of dereferencing it. -xlinka
        var added = Add();
        if (added != null)
            added.Value = value;
    }

    public bool AddUnique(T value)
    {
        if (IndexOf(value) < 0)
        {
            Add(value);
            return true;
        }
        return false;
    }

    public void AddRange(IEnumerable<T> values)
    {
        foreach (T value in values)
        {
            Add(value);
        }
    }

    public void Insert(int index, T value)
    {
        var inserted = Insert(index);
        if (inserted != null)
            inserted.Value = value;
    }

    public int IndexOf(T value)
    {
        return FindIndex(f => SyncCoder.Equals(f.Value, value));
    }

    public void Remove(T value)
    {
        int idx = IndexOf(value);
        if (idx >= 0)
        {
            RemoveAt(idx);
        }
    }

    public int RemoveAll(T value)
    {
        return RemoveAll(f => SyncCoder.Equals(f.Value, value));
    }

    public Sync<T> GetField(int index)
    {
        return GetElement(index);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(GetElementsEnumerator());
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
