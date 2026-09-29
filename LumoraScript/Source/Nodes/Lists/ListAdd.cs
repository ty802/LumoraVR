// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Puts item at the end. Without a list nothing happens.
[Node("Lists/Add")]
public sealed class ListAdd<T> : ActionNode
{
    public static void Run(ScriptList<T>? list, T item) => list?.Add(item);
}
