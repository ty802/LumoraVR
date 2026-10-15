// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Something a released object does not go back under. Let go of a Grabbable and it returns to the parent it
// was taken from, unless the nearest of these at or above that parent covers it: ReturnBlockDepth levels
// below the block's own slot, 0 for that slot alone. Then it goes to the world root instead, so a thing
// lifted out of a socket or off a surface and dropped somewhere else does not jump back into it.
public interface IReleaseReturnBlock
{
    bool BlocksReturn { get; }

    int ReturnBlockDepth { get; }
}

// The walk stops at the first block it meets, covering or not: a nearer block is the one that speaks for
// its subtree. It also stops at a SearchBlock, the boundary another rig owns. Runs once per release.
public static class ReleaseReturnGuard
{
    public static bool Blocks(Slot? parent)
    {
        int depth = 0;
        for (var slot = parent; slot != null && !slot.IsDestroyed && !slot.IsRootSlot; slot = slot.Parent, depth++)
        {
            var components = slot.Components;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] is IReleaseReturnBlock block && !components[i].IsDestroyed)
                    return block.BlocksReturn && depth <= block.ReturnBlockDepth;
            }
            if (slot.GetComponent<SearchBlock>() is { IsDestroyed: false } search && search.Enabled.Value)
                return false;
        }
        return false;
    }
}
