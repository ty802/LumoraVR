// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Spawn the in-world avatar creator in front of you, in the FOCUSED world. Toggles: a second press
// removes the one that is already up.
[ComponentCategory("Hidden")]
public sealed class AvatarStudioWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Avatar Studio";
    protected override string? Icon => "avatar";
    protected override color IconTint => DashTheme.IconBlue;

    // The studio opens with or without a model in the world: finding the model is what it is for.
    // The only thing that can stop it is a world that refuses spawning at all, and that says so.
    protected override string? Unavailable
    {
        get
        {
            var world = FocusedWorld;
            if (world?.RootSlot == null)
                return "No world";
            if (!world.AllowsItemSpawning
                || world.DataModelPermissions?.AllowsDomain(world.LocalUser, DataModelPermissionDomain.Spawn) == false)
                return "Spawning off here";
            return null;
        }
    }

    protected override void Invoke()
    {
        var world = FocusedWorld;
        if (world?.RootSlot == null)
            return;

        var existing = world.RootSlot.GetComponentInChildren<Lumora.Core.Components.Avatar.AvatarStudio>();
        if (existing != null && !existing.IsDestroyed)
        {
            existing.Slot.Destroy();
            return;
        }

        float3 position;
        floatQ rotation;
        var userRoot = world.LocalUser?.Root;
        if (userRoot?.HeadSlot != null)
        {
            // -Z (Backward) is the view direction in our head convention; +Z is behind the user.
            position = userRoot.HeadPosition + userRoot.HeadRotation * (float3.Backward * 1.25f);
            rotation = userRoot.HeadRotation;
        }
        else
        {
            position = new float3(0f, 1f, 1.25f);
            rotation = floatQ.Identity;
        }

        var slot = world.RootSlot.AddSlot("Avatar Studio");
        slot.GlobalPosition = position;
        slot.GlobalRotation = rotation;
        slot.AttachComponent<Lumora.Core.Components.Avatar.AvatarStudio>();
    }
}
