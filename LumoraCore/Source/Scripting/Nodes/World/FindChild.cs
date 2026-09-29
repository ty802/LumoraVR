// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// The nearest slot under slot with exactly this name, searched level by level. Partial takes any name
// holding it, ignore case takes either case, and max depth stops the search that many levels down, 1
// for children only, 0 for no limit.
[Node("World/Slots/Find Child", Capability.WorldRead)]
public sealed class FindChild : ReadNode
{
    [return: Port("child")]
    public static Slot? Run(ScriptContext ctx, Slot? slot, string? name, bool partial = false, bool ignoreCase = false, int maxDepth = 0)
    {
        if (!ScriptReads.Visible(ctx, slot) || string.IsNullOrEmpty(name))
            return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
                    var childName = child.Name.Value ?? string.Empty;
                    if (partial ? childName.Contains(name, comparison) : string.Equals(childName, name, comparison))
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
