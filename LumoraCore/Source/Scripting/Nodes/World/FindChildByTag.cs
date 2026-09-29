// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// The nearest slot under slot whose tag is exactly this text, case included, searched level by level. Max
// depth stops the search that many levels down, 1 for children only, 0 for no limit. An empty tag finds
// nothing.
[Node("World/Slots/Find Child By Tag", Capability.WorldRead)]
public sealed class FindChildByTag : ReadNode
{
    [return: Port("child")]
    public static Slot? Run(ScriptContext ctx, Slot? slot, string? tag, int maxDepth = 0)
    {
        if (!ScriptReads.Visible(ctx, slot) || string.IsNullOrEmpty(tag))
            return null;

        var level = new List<Slot> { slot! };
        var next = new List<Slot>();
        for (int depth = 1; level.Count > 0 && (maxDepth <= 0 || depth <= maxDepth); depth++)
        {
            foreach (var current in level)
            {
                foreach (var child in current.Children)
                {
                    if (!ScriptReads.Visible(ctx, child))
                        continue;
                    if (string.Equals(child.Tag.Value, tag, StringComparison.Ordinal))
                        return child;
                    next.Add(child);
                }
            }
            (level, next) = (next, level);
            next.Clear();
        }
        return null;
    }
}
