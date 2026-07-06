// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Destroys its slot tree when the avatar object it belongs to is dequipped.
// Attach to default-filled pieces (head sphere, hand visuals) so equipping a
// real avatar automatically removes them instead of leaving them parented
// under the manager. - xlinka
[ComponentCategory("Users/Avatar")]
public class DiscardOnDequip : Component, IAvatarEquipReceiver
{
    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot) { }

    public void OnDequip(AvatarSocket slot)
    {
        if (World?.IsAuthority != true)
            return;

        Slot?.RunSynchronously(() =>
        {
            if (Slot != null && !Slot.IsDestroyed)
            {
                using (World?.DataModelPermissions?.EnterSystemBypass())
                    Slot.Destroy();
            }
        });
    }
}
