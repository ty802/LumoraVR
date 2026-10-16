// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

public class SyncRefList<T> : SyncElementList<SyncRef<T>>, IEnumerable<T?> where T : class, IWorldElement
{
    public event Action<SyncRefList<T>>? OnChanged;

    public SyncRefList()
    {
        ElementsAdded += (list, idx, count) =>
        {
            for (int i = idx; i < idx + count; i++)
                GetElement(i).OnTargetChange += OnElementTargetChanged;
            OnChanged?.Invoke(this);
        };
        ElementsRemoving += (list, idx, count) =>
        {
            for (int i = idx; i < idx + count; i++)
                GetElement(i).OnTargetChange -= OnElementTargetChanged;
        };
        ElementsRemoved += (list, idx, count) => OnChanged?.Invoke(this);
    }

    // A reference can resolve long after the list entry arrives over the network. Consumers such
    // as skeleton builders must hear that arrival even though the list's length did not change.
    private void OnElementTargetChanged(SyncRef<T> reference) => OnChanged?.Invoke(this);

    public SyncRefList(IWorldElement owner) : this()
    {
        if (owner?.World != null)
        {
            Initialize(owner.World, owner);
        }
    }

    public new struct Enumerator : IEnumerator<T?>, IDisposable, IEnumerator
    {
        private SyncElementList<SyncRef<T>>.Enumerator _baseEnumerator;

        public T? Current => _baseEnumerator.Current.Target;

        object? IEnumerator.Current => Current;

        internal Enumerator(SyncElementList<SyncRef<T>>.Enumerator baseEnumerator)
        {
            _baseEnumerator = baseEnumerator;
        }

        public void Dispose() => _baseEnumerator.Dispose();

        public bool MoveNext() => _baseEnumerator.MoveNext();

        public void Reset() => _baseEnumerator.Reset();
    }

    public new T? this[int index]
    {
        get => GetElement(index).Target;
        set => GetElement(index).Target = value!;
    }

    public void Add(T? element)
    {
        var added = Add();
        if (added != null)
            added.Target = element!;
    }

    public bool Remove(T? element)
    {
        int index = IndexOf(element);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }
        return false;
    }

    public void Insert(int index, T? element)
    {
        var inserted = Insert(index);
        if (inserted != null)
            inserted.Target = element!;
    }

    public bool Contains(T? element) => IndexOf(element) >= 0;

    public int IndexOf(T? element) => FindIndex(r => ReferenceEquals(r.Target, element));

    public SyncRef<T> GetReference(int index) => GetElement(index);

    public Enumerator GetEnumerator()
    {
        return new Enumerator(GetElementsEnumerator());
    }

    IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"SyncRefList<{typeof(T).Name}>[{Count}]";

    public override void Dispose()
    {
        OnChanged = null;
        base.Dispose();
    }
}
