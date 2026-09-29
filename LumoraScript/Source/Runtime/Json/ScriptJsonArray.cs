// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Lumora.Script;

// A JSON array. Reading, setting, inserting or removing anywhere takes time in the log of its length, and
// an edit shares every item it did not touch with the array it came from. An edit with an index outside
// the array gives false and makes nothing.
public sealed class ScriptJsonArray : ScriptJson
{
    private readonly ImmutableList<ScriptJson> _items;
    private readonly long _size;
    private readonly int _depth;

    private ScriptJsonArray(ScriptCollectionBudget budget, ImmutableList<ScriptJson> items, long size, int depth) : base(budget)
    {
        CheckDepth(depth);
        _items = items;
        _size = size;
        _depth = depth;
        TakeWhole(32 + 48L * items.Count);
    }

    public static readonly ScriptJsonArray Empty = new(ScriptJsonValue.Shared, ImmutableList<ScriptJson>.Empty, 1, 1);

    public static ScriptJsonArray Of(ScriptCollectionBudget budget, IEnumerable<ScriptJson> items)
    {
        var builder = ImmutableList.CreateBuilder<ScriptJson>();
        long size = 1;
        int depth = 1;
        foreach (var item in items)
        {
            builder.Add(item);
            size += item.Size;
            depth = System.Math.Max(depth, item.Depth + 1);
            CheckDepth(depth);
        }
        return new ScriptJsonArray(budget, builder.ToImmutable(), size, depth);
    }

    public override ScriptJsonKind Kind => ScriptJsonKind.Array;

    public override long Size => _size;

    public override int Depth => _depth;

    public override int Count => _items.Count;

    public IReadOnlyList<ScriptJson> Items => _items;

    public ScriptJson? Get(int index) => (uint)index < (uint)_items.Count ? _items[index] : null;

    public ScriptJsonArray Add(ScriptCollectionBudget budget, ScriptJson item)
        => new(budget, _items.Add(item), _size + item.Size, System.Math.Max(_depth, item.Depth + 1));

    public bool TryInsert(ScriptCollectionBudget budget, int index, ScriptJson item, out ScriptJsonArray result)
    {
        result = this;
        if ((uint)index > (uint)_items.Count)
            return false;
        result = new(budget, _items.Insert(index, item), _size + item.Size, System.Math.Max(_depth, item.Depth + 1));
        return true;
    }

    public bool TrySet(ScriptCollectionBudget budget, int index, ScriptJson item, out ScriptJsonArray result)
    {
        result = this;
        if ((uint)index >= (uint)_items.Count)
            return false;
        var old = _items[index];
        var items = _items.SetItem(index, item);
        result = new(budget, items, _size - old.Size + item.Size, DepthAfter(items, old, item));
        return true;
    }

    public bool TryRemoveAt(ScriptCollectionBudget budget, int index, out ScriptJsonArray result)
    {
        result = this;
        if ((uint)index >= (uint)_items.Count)
            return false;
        var old = _items[index];
        var items = _items.RemoveAt(index);
        result = new(budget, items, _size - old.Size, DepthAfter(items, old, null));
        return true;
    }

    // count items from start, both kept inside the array.
    public ScriptJsonArray Slice(ScriptCollectionBudget budget, int start, int count)
    {
        start = System.Math.Clamp(start, 0, _items.Count);
        count = System.Math.Clamp(count, 0, _items.Count - start);
        return Of(budget, _items.GetRange(start, count));
    }

    public ScriptJsonArray Concat(ScriptCollectionBudget budget, ScriptJsonArray other)
        => new(budget, _items.AddRange(other._items), _size + other._size - 1, System.Math.Max(_depth, other._depth));

    public int IndexOf(ScriptJson item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Equals(item))
                return i;
        }
        return -1;
    }

    // What an item's leaving does to the depth: only the deepest going can make it shallower, and only then
    // is the rest walked.
    private int DepthAfter(ImmutableList<ScriptJson> items, ScriptJson removed, ScriptJson? added)
    {
        int depth = added == null ? 1 : System.Math.Max(1, added.Depth + 1);
        if (removed.Depth + 1 < _depth)
            return System.Math.Max(_depth, depth);
        foreach (var item in items)
            depth = System.Math.Max(depth, item.Depth + 1);
        return depth;
    }

    private protected override bool Same(ScriptJson other)
    {
        var array = (ScriptJsonArray)other;
        if (array._items.Count != _items.Count || array._size != _size)
            return false;
        for (int i = 0; i < _items.Count; i++)
        {
            if (!_items[i].Equals(array._items[i]))
                return false;
        }
        return true;
    }

    private protected override int Hash()
    {
        var hash = new HashCode();
        hash.Add(0x5A7);
        foreach (var item in _items)
            hash.Add(item.GetHashCode());
        return hash.ToHashCode();
    }

    internal override void ReachInside(Action<ScriptCollection> reach)
    {
        foreach (var item in _items)
            reach(item);
    }
}
