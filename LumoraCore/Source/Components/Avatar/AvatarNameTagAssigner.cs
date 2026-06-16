// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Tiny coordinator that pushes the user's display data (AvatarManager
// NameTag* fields) into referenced text renderers. The visual itself is
// ordinary mesh text - there is no dedicated nameplate render path. Custom
// avatars can carry their own assigner, which suppresses the auto badge. - xlinka
[ComponentCategory("Users/Common Avatar System")]
public class AvatarNameTagAssigner : Component, IAvatarObjectComponent
{
    public SyncRefList<TextRenderer> LabelTargets { get; private set; } = null!;

    public override void OnAwake()
    {
        base.OnAwake();
        LabelTargets = new SyncRefList<TextRenderer>(this);
    }

    public void OnPreEquip(AvatarObjectSlot slot) { }

    public void OnEquip(AvatarObjectSlot slot)
    {
        var manager = Slot?.GetComponentInParent<AvatarManager>()
                      ?? Slot?.ActiveUserRoot?.GetRegisteredComponent<AvatarManager>();
        if (manager != null)
            UpdateTags(manager);
    }

    public void OnDequip(AvatarObjectSlot slot) { }

    public void UpdateTags(AvatarManager manager)
    {
        // Writes go into synced fields - only the authority pushes, every
        // other peer receives the result through normal sync.
        if (World?.IsAuthority != true || manager == null)
            return;

        foreach (var target in LabelTargets)
        {
            if (target == null || target.IsDestroyed)
                continue;

            target.Text.Value = manager.NameTagText.Value ?? string.Empty;
            target.Color.Value = manager.NameTagColor.Value;
            var o = manager.NameTagOutline.Value;
            target.OutlineColor.Value = new colorHDR(o.r, o.g, o.b, o.a);
        }
    }
}
