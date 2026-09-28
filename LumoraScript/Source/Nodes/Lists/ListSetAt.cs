// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Puts item in place of the one at index. Outside the list nothing changes and replaced is false.
[Node("Lists/Set At")]
public sealed class ListSetAt<T> : ActionNode
{
    [return: Port("replaced")]
    public static bool Run(ScriptList<T>? list, int index, T item) => list != null && list.Set(index, item);
}
