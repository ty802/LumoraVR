// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// A script's list. Out-of-range reads give the type's default and out-of-range writes do nothing, the
// same as every other node that is handed something that is not there. A list that loses most of its
// items lets go of the room they took, so emptying one keeps nothing the budget no longer counts.
public sealed class ScriptList<T> : ScriptCollection, IScriptBoxedList
{
    private static readonly bool HoldsCollections = Nests<T>();

    private readonly List<T> _items = new();

    public ScriptList(ScriptCollectionBudget budget) : base(budget)
    {
    }

    public override int Count => _items.Count;

    public IReadOnlyList<T> Items => _items;

    public T? Get(int index) => (uint)index < (uint)_items.Count ? _items[index] : default;

    public void Add(T item)
    {
        Take(CostOf(item));
        _items.Add(item);
    }

    public bool Insert(int index, T item)
    {
        if ((uint)index > (uint)_items.Count)
            return false;
        Take(CostOf(item));
        _items.Insert(index, item);
        return true;
    }

    public bool Set(int index, T item)
    {
        if ((uint)index >= (uint)_items.Count)
            return false;
        Resize(CostOf(_items[index]), CostOf(item));
        _items[index] = item;
        return true;
    }

    public bool RemoveAt(int index)
    {
        if ((uint)index >= (uint)_items.Count)
            return false;
        Give(CostOf(_items[index]));
        _items.RemoveAt(index);
        Shrink();
        return true;
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        return index >= 0 && RemoveAt(index);
    }

    // One pass to count what goes and one to take it out, however many match.
    public int RemoveAll(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        long bytes = 0;
        foreach (var existing in _items)
        {
            if (comparer.Equals(existing, item))
                bytes += CostOf(existing);
        }
        if (bytes == 0)
            return 0;
        int removed = _items.RemoveAll(existing => comparer.Equals(existing, item));
        Give(bytes);
        Shrink();
        return removed;
    }

    public void Clear()
    {
        Give(Bytes);
        _items.Clear();
        _items.Capacity = 0;
    }

    public int IndexOf(T item) => _items.IndexOf(item);

    public bool Contains(T item) => _items.Contains(item);

    IEnumerable<object?> IScriptBoxedList.BoxedItems()
    {
        foreach (var item in _items)
            yield return item;
    }

    void IScriptBoxedList.AddBoxed(object? item) => Add(item is T typed ? typed : default!);

    internal override void ReachInside(Action<ScriptCollection> reach)
    {
        if (!HoldsCollections)
            return;
        foreach (var item in _items)
        {
            if (item is ScriptCollection collection)
                reach(collection);
        }
    }

    private void Shrink()
    {
        if (_items.Capacity > 64 && _items.Count < _items.Capacity / 4)
            _items.Capacity = _items.Count * 2;
    }
}
