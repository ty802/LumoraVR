// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Opens the create-world dialog that the Home screen owns. Works from a popped-out panel too: it brings
// the dash to Home first, because the dialog is part of that screen.
[ComponentCategory("Hidden")]
public sealed class NewWorldWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "New World";

    protected override bool Primary => true;

    protected override void Invoke()
    {
        var dash = Dash;
        if (dash == null)
            return;
        foreach (var screen in dash.Screens)
        {
            if (screen is not HomeScreen home)
                continue;
            dash.SwitchTo(home);
            home.OpenCreateMenu();
            return;
        }
    }
}

// Close the world you are in. The confirm, the fallback world and the focus hand-off all belong to the
// Worlds screen, so this is a shortcut to that path rather than a second copy of it.
[ComponentCategory("Hidden")]
public sealed class LeaveWorldWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Leave World";

    protected override string? Unavailable
    {
        get
        {
            var world = FocusedWorld;
            if (world == null)
                return "No world";
            return IsLocalHome(world) ? "You are home" : null;
        }
    }

    protected override void Invoke()
    {
        var world = FocusedWorld;
        var dash = Dash;
        if (world == null || dash == null)
            return;
        foreach (var screen in dash.Screens)
        {
            if (screen is WorldsScreen worlds)
            {
                worlds.CloseWorld(world);
                return;
            }
        }
    }
}

// Spawn the in-world avatar creator in front of you, in the FOCUSED world. Toggles: a second press
// removes the one that is already up.
[ComponentCategory("Hidden")]
public sealed class AvatarStudioWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Avatar Studio";

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

// Import whatever is on the OS clipboard into the focused world. Same gate the world-side Ctrl+V
// importer uses, so the button is honest about when a paste would actually land.
[ComponentCategory("Hidden")]
public sealed class PasteWidgetPreset : ActionWidgetPreset
{
    protected override string Title => "Paste";

    protected override string? Unavailable
    {
        get
        {
            if (ImportHandlers.Clipboard == null)
                return "No clipboard";
            var world = FocusedWorld;
            if (world == null)
                return "No world";
            if (!world.IsAuthority)
                return "Host only";
            return world.State == World.WorldState.Running ? null : "World loading";
        }
    }

    protected override void Invoke() => ImportHandlers.Clipboard?.Paste();
}
