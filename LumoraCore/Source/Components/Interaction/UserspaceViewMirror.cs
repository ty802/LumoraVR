// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Keeps the userspace pointer rig sitting exactly where the focused user is standing. The rig's
// hands are tracked to the real controller devices, so for the VR rays to actually land on the
// dash the whole rig has to overlay the focused user's body. We copy the focused world's user-root
// world pose every late update - after locomotion and physics have moved them, so the rig doesn't
// trail by a frame. When nothing is focused (you just deleted the world you were in) we hold the
// last pose, which is what keeps the dash pointable through that gap. Desktop doesn't depend on any
// of this: its pointer aims off the flat free-cursor ray and ignores the rig pose entirely. -xlinka
[ComponentCategory("Interaction")]
public sealed class UserspaceViewMirror : Component
{
    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);

        var focused = Engine.Current?.WorldManager?.FocusedWorld;
        var root = focused?.LocalUser?.Root;
        if (root?.Slot == null || root.Slot.IsDestroyed) return;

        // Engine-driven body positioning in a local overlay world, not a user edit - bypass the
        // permission gate so the per-frame transform writes never trip a denial. -xlinka
        using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
        Slot.GlobalPosition = root.Slot.GlobalPosition;
        Slot.GlobalRotation = root.Slot.GlobalRotation;
    }
}
