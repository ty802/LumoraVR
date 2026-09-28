// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

[Node("Dictionaries/Contains Key")]
public sealed class DictContainsKey<TKey, TValue> : ReadNode where TKey : notnull
{
    [return: Port("found")]
    public static bool Run(ScriptDictionary<TKey, TValue>? dictionary, TKey? key) => dictionary != null && dictionary.ContainsKey(key);
}
