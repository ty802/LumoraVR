// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// The CUSTOM-AVATAR nameplate path: an avatar that wants to draw its own name brings one of these and
// points it at its own text renderers, and NameplateManager stands down for that user (it looks for one
// of these on the avatar tree and hides itself when it finds one).
//
// The built-in plate does not use this. It is composed locally per peer and reads the same
// AvatarEquipManager fields directly, so nothing here is on the path of an ordinary user. What this does
// have that the built-in plate does not is REPLICATED writes: the targets are synced text on the
// authored avatar, so only the authority pushes and every other peer receives the result. -xlinka
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
