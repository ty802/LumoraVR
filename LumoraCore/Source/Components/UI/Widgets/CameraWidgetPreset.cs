// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// This machine's camera from Home. Off, a press puts it in the FOCUSED world and opens its panel; on with
// the panel put away, a press brings the panel back; on with the panel up, a press takes both away. The
// camera itself is the director's, and everything else about it is on the panel. -xlinka
[ComponentCategory("Hidden")]
public sealed class CameraWidgetPreset : ActionWidgetPreset
{
    private static CameraDirector? Director => CameraDirector.Local is { IsDestroyed: false } director ? director : null;

    protected override string Title => Director is { Active: true } ? "Camera on" : "Camera";
    protected override string? Icon => "camera";
    protected override color IconTint => DashTheme.IconBlue;

    protected override string? Unavailable
    {
        get
        {
            // The checks are about putting a camera in; taking one away is always allowed.
            if (Director is { Active: true })
                return null;
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
        var director = Director;
        if (director == null)
            return;
        if (!director.Active)
        {
            director.Active = true;
            CameraPanel.Open();
        }
        else if (!CameraPanel.IsOpen)
        {
            CameraPanel.Open();
        }
        else
        {
            director.Active = false;
            CameraPanel.Remove();
        }
    }
}
