// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// An empty list. It is in the flow, so every node after it that reads its list reads this one list.
[Node("Lists/New List")]
public sealed class NewList<T> : ActionNode
{
    [return: Port("list")]
    public static ScriptList<T> Run(ScriptContextBase ctx) => new(ctx.Collections);
}
