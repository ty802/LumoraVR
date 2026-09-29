// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// A script's dictionary. Keys keep the order they were first added in, so walking the keys gives the same
// order on every machine. A null key is refused. One that loses most of its keys lets go of the room they
// took.
public sealed class ScriptDictionary<TKey, TValue> : ScriptCollection, IScriptBoxedDictionary where TKey : notnull
{
    private static readonly bool HoldsCollections = Nests<TKey>() || Nests<TValue>();

    private readonly Dictionary<TKey, TValue> _values = new();
    private readonly List<TKey> _order = new();

    public ScriptDictionary(ScriptCollectionBudget budget) : base(budget)
    {
    }

    public override int Count => _values.Count;

    public IReadOnlyList<TKey> Keys => _order;

    public bool TryGet(TKey? key, out TValue? value)
    {
        value = default;
        if (key is null || !_values.TryGetValue(key, out var found))
            return false;
        value = found;
        return true;
    }

    public bool ContainsKey(TKey? key) => key is not null && _values.ContainsKey(key);

    // Adds only a key that is not there yet.
    public bool Add(TKey? key, TValue value)
    {
        if (key is null || _values.ContainsKey(key))
            return false;
        Take(CostOf(key) + CostOf(value));
        _values.Add(key, value);
        _order.Add(key);
        return true;
    }

    // Adds or replaces.
    public void Set(TKey? key, TValue value)
    {
        if (key is null)
            return;
        if (_values.TryGetValue(key, out var old))
        {
            Resize(CostOf(old), CostOf(value));
            _values[key] = value;
            return;
        }
        Add(key, value);
    }

    public bool Remove(TKey? key)
    {
        if (key is null || !_values.TryGetValue(key, out var old))
            return false;
        Give(CostOf(key) + CostOf(old));
        _values.Remove(key);
        _order.Remove(key);
        if (_order.Capacity > 64 && _order.Count < _order.Capacity / 4)
        {
            _order.Capacity = _order.Count * 2;
            _values.TrimExcess(_order.Count * 2);
        }
        return true;
    }

    public void Clear()
    {
        Give(Bytes);
        _values.Clear();
        _values.TrimExcess();
        _order.Clear();
        _order.Capacity = 0;
    }

    public TValue? ValueAt(int index)
        => (uint)index < (uint)_order.Count ? _values[_order[index]] : default;

    public TKey? KeyAt(int index) => (uint)index < (uint)_order.Count ? _order[index] : default;

    IEnumerable<(object Key, object? Value)> IScriptBoxedDictionary.BoxedEntries()
    {
        foreach (var key in _order)
            yield return (key, _values[key]);
    }

    void IScriptBoxedDictionary.SetBoxed(object key, object? value) => Set((TKey)key, value is TValue typed ? typed : default!);

    internal override void ReachInside(Action<ScriptCollection> reach)
    {
        if (!HoldsCollections)
            return;
        foreach (var (key, value) in _values)
        {
            if (key is ScriptCollection inKey)
                reach(inKey);
            if (value is ScriptCollection inValue)
                reach(inValue);
        }
    }
}
