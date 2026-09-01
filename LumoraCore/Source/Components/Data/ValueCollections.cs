// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Data;

// Storage components: one collection member each, nothing attached to it.
//
// Same reason ValueHolder exists, one dimension up. Building anything list-shaped without code -
// a spawn table, a set of waypoints, a palette several materials read - otherwise means hijacking an
// unrelated component's collection, and there is exactly one of those per slot to hijack. -xlinka

[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueList<T> : Component
{
    public readonly SyncFieldList<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueList()
    {
        Values = new SyncFieldList<T>(this);
    }
}

// Flat buffer rather than one element per entry: no RefID per value, and about 25x less memory than
// the list above. The cost is that entries are raw values, so nothing can drive or reference one.
[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueArray<T> : Component
{
    public readonly SyncArray<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueArray()
    {
        Values = new SyncArray<T>();
    }
}

// Rows of a fixed width over the same flat buffer - a grid of tiles, a table of samples.
[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueGrid<T> : Component
{
    public readonly SyncGrid<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueGrid()
    {
        Values = new SyncGrid<T>();
    }
}

// String keys because the component browser only offers single-argument generics, and a name is the
// key content authors actually reach for. Values are raw, same tradeoff as ValueArray. -xlinka
[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueDictionary<T> : Component
{
    public readonly SyncValueDictionary<string, T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueDictionary()
    {
        Values = new SyncValueDictionary<string, T>();
    }
}
