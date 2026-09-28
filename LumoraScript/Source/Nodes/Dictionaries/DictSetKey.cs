// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Gives key this value, adding the key when it is new; a key already there keeps its place in the order.
// A key that holds nothing is refused.
[Node("Dictionaries/Set Key")]
public sealed class DictSetKey<TKey, TValue> : ActionNode where TKey : notnull
{
    public static void Run(ScriptDictionary<TKey, TValue>? dictionary, TKey? key, TValue value)
    {
        if (dictionary == null)
            return;
        if (key is null)
            throw new UnauthorizedAccessException("a dictionary key cannot be null");
        dictionary.Set(key, value);
    }
}
