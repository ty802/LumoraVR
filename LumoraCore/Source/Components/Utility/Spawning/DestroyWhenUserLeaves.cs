// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

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
