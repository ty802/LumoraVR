// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Takes out every item equal to item; removed is how many went.
[Node("Lists/Remove All")]
public sealed class ListRemoveAll<T> : ActionNode
{
    [return: Port("removed")]
    public static int Run(ScriptList<T>? list, T item) => list?.RemoveAll(item) ?? 0;
}
