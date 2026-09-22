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
