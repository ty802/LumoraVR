// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Empties the dictionary and gives the room its keys and values took back to the budget.
[Node("Dictionaries/Clear")]
public sealed class DictClear<TKey, TValue> : ActionNode where TKey : notnull
{
    public static void Run(ScriptDictionary<TKey, TValue>? dictionary) => dictionary?.Clear();
}
