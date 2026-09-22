// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Data;

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
