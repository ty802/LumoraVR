// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// Network-synchronized list of types. Each entry is a SyncType element, so membership replicates and
// persists through the normal element-list machinery and every entry encodes by name.
public class SyncTypeList : SyncElementList<SyncType>, IEnumerable<Type>, IEnumerable
{
    public event Action<SyncTypeList>? OnChanged;

    public SyncTypeList()
    {
        ElementsAdded += (list, idx, count) => OnChanged?.Invoke(this);
        ElementsRemoved += (list, idx, count) => OnChanged?.Invoke(this);
    }

    public SyncTypeList(IWorldElement owner) : this()
    {
        if (owner?.World != null)
        {
            Initialize(owner.World, owner);
        }
    }

    // Yields the types instead of the backing SyncType elements.
    public new struct Enumerator : IEnumerator<Type>, IDisposable, IEnumerator
    {
        private SyncElementList<SyncType>.Enumerator _baseEnumerator;

        public Type Current => _baseEnumerator.Current.Value;

        object IEnumerator.Current => Current;

        internal Enumerator(SyncElementList<SyncType>.Enumerator baseEnumerator)
        {
            _baseEnumerator = baseEnumerator;
        }

        public void Dispose() => _baseEnumerator.Dispose();

        public bool MoveNext() => _baseEnumerator.MoveNext();

        public void Reset() => _baseEnumerator.Reset();
    }

    public new Type this[int index]
    {
        get => GetElement(index).Value;
        set => GetElement(index).Value = value;
    }

    public void Add(Type type)
    {
        Add().Value = type;
    }

    public bool AddUnique(Type type)
    {
        if (IndexOf(type) >= 0)
            return false;
        Add(type);
        return true;
    }

    public void AddRange(IEnumerable<Type> types)
    {
        foreach (var type in types)
            Add(type);
    }

    public void Insert(int index, Type type)
    {
        Insert(index).Value = type;
    }

    public int IndexOf(Type type) => FindIndex(entry => entry.Value == type);

    public bool Contains(Type type) => IndexOf(type) >= 0;

    public bool Remove(Type type)
    {
        int index = IndexOf(type);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public int RemoveAll(Type type) => RemoveAll(entry => entry.Value == type);

    // The backing element, for callers that need the member itself (drives, reference targets).
    public SyncType GetField(int index) => GetElement(index);

    public Enumerator GetEnumerator() => new Enumerator(GetElementsEnumerator());

    IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"SyncTypeList[{Count}]";

    public override void Dispose()
    {
        OnChanged = null;
        base.Dispose();
    }
}
