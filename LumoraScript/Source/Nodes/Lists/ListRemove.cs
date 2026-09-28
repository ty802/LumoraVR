// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Takes out the first item equal to item: the same text exactly, the same numbers, the same object.
[Node("Lists/Remove")]
public sealed class ListRemove<T> : ActionNode
{
    [return: Port("removed")]
    public static bool Run(ScriptList<T>? list, T item) => list != null && list.Remove(item);
}
