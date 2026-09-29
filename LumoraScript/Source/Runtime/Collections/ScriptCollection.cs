// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Script;

// A list, dictionary or JSON value a script made. It lives in the instance's memory only: never saved, never
// synced.
public abstract class ScriptCollection
{
    private readonly ScriptCollectionBudget _budget;

    protected ScriptCollection(ScriptCollectionBudget budget)
    {
        _budget = budget;
        budget.Track(this);
    }

    // The room it takes, as the budget counts it.
    public long Bytes { get; private set; }

    public abstract int Count { get; }

    internal ScriptCollectionBudget Budget => _budget;

    // Every list or dictionary this one holds, for the budget's count of what a script can still reach.
    internal virtual void ReachInside(Action<ScriptCollection> reach)
    {
    }

    protected void Take(long bytes)
    {
        if (Count == ScriptCollectionBudget.WarnItems)
            _budget.Warn(ScriptWarning.CollectionItems, $"a list or dictionary past {ScriptCollectionBudget.WarnItems:N0} items");
        _budget.Take(bytes);
        Bytes += bytes;
    }

    // All the room at once, for a collection made whole that never changes after: a JSON value.
    protected void TakeWhole(long bytes)
    {
        _budget.Take(bytes);
        Bytes += bytes;
    }

    protected void Resize(long before, long after)
    {
        if (after > before)
            _budget.Take(after - before);
        else
            _budget.Give(before - after);
        Bytes += after - before;
    }

    protected void Give(long bytes)
    {
        _budget.Give(bytes);
        Bytes -= bytes;
    }

    // Text is the one thing an item carries past its own size: a string's characters, or an address's.
    protected static long CostOf<T>(T item)
    {
        long cost = 16 + (typeof(T).IsValueType ? Unsafe.SizeOf<T>() : 0);
        if (item is string text)
            cost += 24 + 2L * text.Length;
        else if (item is Uri address)
            cost += 24 + 2L * address.OriginalString.Length;
        return cost;
    }

    protected static bool Nests<T>() => typeof(ScriptCollection).IsAssignableFrom(typeof(T));
}
