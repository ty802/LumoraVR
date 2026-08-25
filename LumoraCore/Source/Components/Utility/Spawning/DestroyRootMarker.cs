// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Marks this slot as the thing a delete request from anywhere beneath it should remove.
//
// Without a marker a delete button can only destroy the slot it was handed, so a button sitting three
// slots deep inside a prop deletes its own panel and leaves the prop standing. The marker moves that
// decision to whoever built the prop.
//
// Distinct from the ObjectRoot component: that one carries an object's identity and its
// grabbable/duplicatable/destroyable policy, and is placed by the import and spawn paths. This says
// only "delete requests stop here", so a builder can mark a sub-assembly deletable on its own without
// declaring it a whole separate object. -xlinka
[ComponentCategory("Utility/Spawning")]
[SingleInstancePerSlot]
public class DestroyRootMarker : Component
{
    // Falls back to the slot itself when nothing above it is marked, so a delete request never
    // silently does nothing.
    public static Slot? FindRoot(Slot? slot)
    {
        if (slot == null)
            return null;

        // Nearest, not outermost: a marked sub-assembly should be deletable on its own without the
        // delete escalating to whatever larger thing it was dropped into.
        var current = slot;
        while (current != null && !current.IsDestroyed)
        {
            if (current.GetComponent<DestroyRootMarker>() != null)
                return current;
            current = current.Parent;
        }
        return slot;
    }
}

[ComponentCategory("Utility/Spawning")]
public class DestroyWhenUserLeaves : Component
{
    public readonly SyncRef<User> TargetUser;

    public DestroyWhenUserLeaves()
    {
        TargetUser = new SyncRef<User>(this);
    }

    public override void OnUserLeft(User user)
    {
        // RawTarget, not Target: the departing user is already being torn down, so the filtered getter
        // has started reading as null and the comparison would never match.
        if (World?.IsAuthority != true || user != TargetUser.RawTarget)
            return;

        var slot = Slot;
        if (slot != null && !slot.IsDestroyed && !slot.IsRootSlot)
            slot.Destroy();
    }
}
