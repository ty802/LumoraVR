// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Puts item at index and moves the rest along; an index equal to the count adds at the end. Anywhere else
// nothing changes and inserted is false.
[Node("Lists/Insert At")]
public sealed class ListInsertAt<T> : ActionNode
{
    [return: Port("inserted")]
    public static bool Run(ScriptList<T>? list, int index, T item) => list != null && list.Insert(index, item);
}
