// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

// Contributes avatar actions to the context menu: "Equip Avatar" when the
// summoning laser points at an avatar tree, "Dequip Avatar" while one is
// worn. Equips route through AvatarEquipManager's body-node dispatch - the menu
// is just the confirm step, mirroring the touch-and-confirm flow. - xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarContextActions : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        var userRoot = Slot?.ActiveUserRoot;
        var manager = userRoot?.GetRegisteredComponent<AvatarEquipManager>();
        if (manager == null)
            return;

        var avatarRoot = FindAvatarRoot(context?.Target);

        if (avatarRoot != null && !avatarRoot.IsEquipped && avatarRoot.Slot != manager.CurrentAvatar.Target)
        {
            var targetSlot = avatarRoot.Slot;
            page.AddItem(new ContextMenuItem
            {
                Label = "Equip Avatar",
                FillColor = new[] { 0.14f, 0.30f, 0.18f, 0.92f },
                OnPressed = _ =>
                {
                    if (!manager.EquipAvatar(targetSlot))
                        LumoraLogger.Warn("AvatarContextActions: Equip failed");
                },
            });
        }

    }

    // The laser usually hits a mesh deep inside the avatar; walk up for the
    // AvatarForm tag. Stops at user roots so worn avatars don't offer
    // themselves.
    private static AvatarForm? FindAvatarRoot(Slot? hit)
    {
        var current = hit;
        while (current != null)
        {
            var root = current.GetComponent<AvatarForm>();
            if (root != null)
                return root;
            if (current.GetComponent<UserRoot>() != null)
                return null;
            current = current.Parent;
        }
        return null;
    }
}
