// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Adds key with this value only when the dictionary does not have it yet; added is false, and the value
// already there stays, when it does. A key that holds nothing is refused.
[Node("Dictionaries/Add Key")]
public sealed class DictAddKey<TKey, TValue> : ActionNode where TKey : notnull
{
    [return: Port("added")]
    public static bool Run(ScriptDictionary<TKey, TValue>? dictionary, TKey? key, TValue value)
    {
        if (dictionary == null)
            return false;
        if (key is null)
            throw new UnauthorizedAccessException("a dictionary key cannot be null");
        return dictionary.Add(key, value);
    }
}
