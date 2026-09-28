// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Takes out key and its value; removed is false when the dictionary did not have it.
[Node("Dictionaries/Remove Key")]
public sealed class DictRemoveKey<TKey, TValue> : ActionNode where TKey : notnull
{
    [return: Port("removed")]
    public static bool Run(ScriptDictionary<TKey, TValue>? dictionary, TKey? key) => dictionary != null && dictionary.Remove(key);
}
