// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Interaction;

// Pins the hierarchy beneath this slot: grab release handlers, drop targets and the inspector's
// drag-to-reparent all leave anything inside it alone.
//
// MaxDepth limits the reach. 0 protects only this slot, 1 also its direct children, and the default
// protects the whole subtree. Put it on the root of a machine whose parts must stay where they are
// while still being grabbable and movable.
//
// This is a courtesy to other systems, not a lock. Nothing stops a tool or an inspector from
// setting the parent directly, and the permission gate is still the thing that decides who may edit
// what. It exists so ambient behaviour does not quietly rearrange an authored assembly. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public class GrabReparentBlock : Component, IReparentBlock, ICustomInspectorUI
{
    // Off leaves the subtree free to move.
    public readonly Sync<bool> Block;

    // 0 is this slot only.
    public readonly Sync<int> MaxDepth;

    public GrabReparentBlock()
    {
        Block = new Sync<bool>(this, true);
        MaxDepth = new Sync<int>(this, int.MaxValue);
    }

    public bool AllowsReparent(Slot target, Slot newParent)
    {
        if (!Block.Value)
            return true;
        int depth = DepthBelow(target);
        return depth < 0 || depth > MaxDepth.Value;
    }

    // Hops from the block's own slot down to the target. -1 when the target is not underneath us at
    // all, which happens whenever the guard's climb reaches a block that is a cousin rather than an
    // ancestor.
    private int DepthBelow(Slot? target)
    {
        var own = Slot;
        if (own == null || target == null)
            return -1;

        int depth = 0;
        for (var slot = target; slot != null && !slot.IsDestroyed; slot = slot.Parent)
        {
            if (ReferenceEquals(slot, own))
                return depth;
            depth++;
        }
        return -1;
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "State", Block.Value ? "blocking" : "off");
        InspectorStats.AddRow(ui, "Reach",
            MaxDepth.Value == int.MaxValue ? "whole subtree" : $"{MaxDepth.Value} level(s) down");
    }
}
