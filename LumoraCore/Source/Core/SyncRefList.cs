// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

/// <summary>
/// Network-synchronized list of references to world elements.
/// Each entry is a <see cref="SyncRef{T}"/> element, so membership replicates and
/// persists through the normal sync-element machinery (RefID allocation, delta encode,
/// validation, save/load). Null entries are allowed and map to a cleared reference.
/// </summary>
public class SyncRefList<T> : SyncElementList<SyncRef<T>>, IEnumerable<T?> where T : class, IWorldElement
{
    /// <summary>
    /// Event triggered when the membership of this list changes (add, remove, clear).
    /// Does not fire when the value of a referenced element changes.
    /// </summary>
    public event Action<SyncRefList<T>>? OnChanged;

    public SyncRefList()
    {
        ElementsAdded += (list, idx, count) => OnChanged?.Invoke(this);
        ElementsRemoved += (list, idx, count) => OnChanged?.Invoke(this);
    }

    /// <summary>
    /// Create a reference list owned by a world element, initializing it immediately when
    /// the owner already has a world (matches the SyncFieldList construction pattern).
    /// </summary>
    public SyncRefList(IWorldElement owner) : this()
    {
        if (owner?.World != null)
        {
            Initialize(owner.World, owner);
        }
    }

    /// <summary>
    /// Enumerator that yields the referenced targets instead of the backing SyncRef elements.
    /// </summary>
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

    /// <summary>
    /// Get or set the referenced target at the given index.
    /// </summary>
    public new T? this[int index]
    {
        get => GetElement(index).Target;
        set => GetElement(index).Target = value!;
    }

    /// <summary>
    /// Add a reference to the given element (null is permitted).
    /// </summary>
    public void Add(T? element)
    {
        Add().Target = element!;
    }

    /// <summary>
    /// Remove the first entry referencing the given element.
    /// Returns true if an entry was removed.
    /// </summary>
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

    /// <summary>
    /// Insert a reference to the given element at the index.
    /// </summary>
    public void Insert(int index, T? element)
    {
        Insert(index).Target = element!;
    }

    /// <summary>
    /// Whether any entry references the given element.
    /// </summary>
    public bool Contains(T? element) => IndexOf(element) >= 0;

    /// <summary>
    /// Index of the first entry referencing the given element, or -1.
    /// </summary>
    public int IndexOf(T? element) => FindIndex(r => ReferenceEquals(r.Target, element));

    /// <summary>
    /// Get the backing SyncRef element at the index.
    /// </summary>
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
