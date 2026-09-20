// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// Per-avatar clip planes for the wearer's eyes. Rides the avatar and registers itself on the wearer's
// user root when the avatar is equipped, so an avatar with long whiskers or a tiny one can push the
// near plane where its geometry needs it. Both values are optional: null leaves the engine default
// alone. Only the wearer's own eye cameras read this; in-world cameras keep their own planes.
[ComponentCategory("Users/Avatar")]
public class AvatarRenderSettings : Component, IRenderSettingsSource, IAvatarEquipReceiver
{
    public readonly Sync<float?> NearClip = new();

    public readonly Sync<float?> FarClip = new();

    float? IRenderSettingsSource.NearClip => NearClip.Value;

    float? IRenderSettingsSource.FarClip => FarClip.Value;

    public void OnPreEquip(AvatarSocket slot) { }

    public void OnEquip(AvatarSocket slot)
    {
        var userRoot = slot?.Slot?.ActiveUserRoot;
        if (userRoot == null || userRoot.IsDestroyed)
            return;
        userRoot.RenderSettings.Target = this;
    }

    public void OnDequip(AvatarSocket slot)
    {
        var userRoot = slot?.Slot?.ActiveUserRoot;
        if (userRoot == null || userRoot.IsDestroyed)
            return;
        if (userRoot.RenderSettings.Target == this)
            userRoot.RenderSettings.Target = null!;
    }

    public override void OnDestroy()
    {
        // The socket does not always get a dequip on the way out (a worn avatar deleted outright), so
        // make sure the root is not left pointing at a dead source.
        var userRoot = Slot?.ActiveUserRoot;
        if (userRoot != null && !userRoot.IsDestroyed && userRoot.RenderSettings.Target == this)
            userRoot.RenderSettings.Target = null!;
        base.OnDestroy();
    }
}
