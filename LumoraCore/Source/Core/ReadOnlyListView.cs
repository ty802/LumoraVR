// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Lumora.Core;

// A live read-only window on a list whose foreach allocates nothing. AsReadOnly hands back an interface,
// and a foreach over an interface boxes the list's enumerator every time it starts; slot children and
// components are walked like that all over the engine, every frame. The list interfaces are still there for
// LINQ and for anything that takes an IReadOnlyList. -xlinka
public sealed class ReadOnlyListView<T> : IReadOnlyList<T>, IList<T>
{
    private readonly List<T> _list;

    public ReadOnlyListView(List<T> list)
    {
        _list = list;
    }

    public int Count => _list.Count;

    public T this[int index] => _list[index];

    public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();

    public bool Contains(T item) => _list.Contains(item);

    public int IndexOf(T item) => _list.IndexOf(item);

    public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    bool ICollection<T>.IsReadOnly => true;

    T IList<T>.this[int index]
    {
        get => _list[index];
        set => throw new NotSupportedException("Read-only view.");
    }

    void IList<T>.Insert(int index, T item) => throw new NotSupportedException("Read-only view.");

    void IList<T>.RemoveAt(int index) => throw new NotSupportedException("Read-only view.");

    void ICollection<T>.Add(T item) => throw new NotSupportedException("Read-only view.");

    void ICollection<T>.Clear() => throw new NotSupportedException("Read-only view.");

    bool ICollection<T>.Remove(T item) => throw new NotSupportedException("Read-only view.");

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _list.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
}
