// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// Body tracker calibration on the default menu: "Calibrate Body" with a headset and a tracker on,
// "Cancel Calibration" while one runs.
[ComponentCategory("Users")]
public class BodyCalibrationContextActions : ContextMenuItemSource
{
    private static readonly float[] CalibrateFill = { 0.10f, 0.22f, 0.30f, 0.92f };
    private static readonly float[] CancelFill = { 0.32f, 0.10f, 0.10f, 0.92f };

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var userRoot = Slot?.ActiveUserRoot;
        if (userRoot == null || World == null)
            return;

        var world = World;
        bool cancels = TrackerCalibration.FindActive(world) != null;

        // Cancel stays reachable when the last tracker or the headset drops out mid-calibration, or the
        // user is left frozen in the pose.
        if (!cancels && !TrackerCalibration.TrackersReady(out _))
            return;

        // Kept open so a refusal can show on the hub; a start closes the ring itself, so the user sees
        // the avatar it puts in the pose. The press does what the label said when the ring opened.
        page.AddItem(new ContextMenuItem
        {
            Group = ContextMenuGroup.Self,
            Label = cancels ? "Cancel Calibration" : "Calibrate Body",
            IconPath = cancels ? null : "avatar",
            FillColor = cancels ? CancelFill : CalibrateFill,
            KeepsOpen = true,
            OnPressed = item =>
            {
                if (cancels)
                {
                    TrackerCalibration.FindActive(world)?.Cancel();
                    userRoot.ContextMenu?.Close();
                    return;
                }

                if (!TrackerCalibration.TryBeginForLocalUser(world, out string reason))
                {
                    item.Detail = reason;
                    return;
                }
                userRoot.ContextMenu?.Close();
            },
        });
    }
}
