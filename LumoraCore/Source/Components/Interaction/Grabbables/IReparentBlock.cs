// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Veto on moving a slot to a new parent. Asked of every block sitting on or above the slot being
// moved. Implementors are Components.
public interface IReparentBlock
{
    // Return false to refuse this particular move and leave the slot where it is. newParent is
    // where it would have gone, so a block may refuse a destination rather than the move itself.
    bool AllowsReparent(Slot target, Slot newParent);
}

// The one place that decides whether a slot may be moved under a new parent.
//
// Every system that reparents world objects on the user's behalf rather than at their direct
// instruction - a drop target, a release handler, a tree drag - has to agree on the answer, or a
// socket that refuses the inspector's drag still loses its item to the next release. So the walk
// lives here and everyone calls it instead of each carrying its own list of exceptions.
//
// The cycle and root checks are in here too, not just the block walk. They are the same three
// mistakes every caller was making separately, and a caller that forgets one of them corrupts the
// hierarchy rather than merely being rude about it. -xlinka
public static class ReparentGuard
{
    public static bool CanReparent(Slot? target, Slot? newParent)
    {
        if (target == null || target.IsDestroyed || target.IsRootSlot)
            return false;
        if (newParent == null || newParent.IsDestroyed)
            return false;
        if (ReferenceEquals(target, newParent) || newParent.IsDescendantOf(target))
            return false;
        if (ReferenceEquals(target.Parent, newParent))
            return false;

        // Blocks sit ABOVE what they protect (a socket parents its item, a marked root parents its
        // subtree), so the walk starts at the target and climbs. The target itself is included: a
        // block on the very slot being moved is the shortest way to pin one object.
        for (var slot = target; slot != null && !slot.IsDestroyed; slot = slot.Parent)
        {
            foreach (var block in slot.GetComponentsImplementing<IReparentBlock>())
            {
                if (block is Component component && (component.IsDestroyed || !component.Enabled.Value))
                    continue;
                if (!block.AllowsReparent(target, newParent))
                    return false;
            }
        }
        return true;
    }
}
