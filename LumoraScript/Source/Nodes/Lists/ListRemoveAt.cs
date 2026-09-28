// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Takes out the item at index and moves the rest back. Outside the list nothing changes and removed is
// false.
[Node("Lists/Remove At")]
public sealed class ListRemoveAt<T> : ActionNode
{
    [return: Port("removed")]
    public static bool Run(ScriptList<T>? list, int index) => list != null && list.RemoveAt(index);
}
