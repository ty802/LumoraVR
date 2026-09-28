// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Empties the list and gives the room its items took back to the budget.
[Node("Lists/Clear")]
public sealed class ListClear<T> : ActionNode
{
    public static void Run(ScriptList<T>? list) => list?.Clear();
}
