// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Data;

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
