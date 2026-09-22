// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Data;

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
