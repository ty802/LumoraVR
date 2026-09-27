// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Null past the last child.
[Node("World/Slots/Child", Capability.WorldRead)]
public sealed class ChildAt : ReadNode
{
    [return: Port("child")]
    public static Slot? Run(ScriptContext ctx, Slot? slot, int index)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return null;
        var children = slot!.Children;
        if (index < 0 || index >= children.Count)
            return null;
        var child = children[index];
        return ScriptReads.Visible(ctx, child) ? child : null;
    }
}
