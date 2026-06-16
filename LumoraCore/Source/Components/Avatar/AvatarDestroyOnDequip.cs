// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Destroys its slot tree when the avatar object it belongs to is dequipped.
// Attach to default-filled pieces (head sphere, hand visuals) so equipping a
// real avatar automatically removes them instead of leaving them parented
// under the manager. - xlinka
[ComponentCategory("Users/Common Avatar System")]
public class AvatarDestroyOnDequip : Component, IAvatarObjectComponent
{
    public void OnPreEquip(AvatarObjectSlot slot) { }

    public void OnEquip(AvatarObjectSlot slot) { }

    public void OnDequip(AvatarObjectSlot slot)
    {
        if (World?.IsAuthority != true)
            return;

        Slot?.RunSynchronously(() =>
        {
            if (Slot != null && !Slot.IsDestroyed)
                Slot.Destroy();
        });
    }
}
