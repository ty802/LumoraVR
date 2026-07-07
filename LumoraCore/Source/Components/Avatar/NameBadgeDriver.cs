// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Tiny coordinator that pushes the user's display data (AvatarEquipManager
// NameTag* fields) into referenced text renderers. The visual itself is
// ordinary mesh text - there is no dedicated nameplate render path. Custom
// avatars can carry their own assigner, which suppresses the auto badge. - xlinka
[ComponentCategory("Users/Avatar")]
public class NameBadgeDriver : Component, IAvatarEquipReceiver
{
    public readonly SyncRefList<TextRenderer> LabelTargets;

    public NameBadgeDriver()
    {
        LabelTargets = new SyncRefList<TextRenderer>(this);
    }

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        var manager = Slot?.GetComponentInParent<AvatarEquipManager>()
                      ?? Slot?.ActiveUserRoot?.GetRegisteredComponent<AvatarEquipManager>();
        if (manager != null)
            UpdateBadge(manager);
    }

    public void OnDequip(AvatarSocket slot) { }

    public void UpdateBadge(AvatarEquipManager manager)
    {
        // Writes go into synced fields - only the authority pushes, every
        // other peer receives the result through normal sync.
        if (World?.IsAuthority != true || manager == null)
            return;

        foreach (var target in LabelTargets)
        {
            if (target == null || target.IsDestroyed)
                continue;

            target.Text.Value = manager.BadgeText.Value ?? string.Empty;
            target.Color.Value = manager.BadgeColor.Value;
            var o = manager.BadgeOutline.Value;
            target.OutlineColor.Value = new colorHDR(o.r, o.g, o.b, o.a);
        }
    }
}
