// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// Scale items for the default menu: at default scale a toggle for the user's
// scaling permission, otherwise "Reset Scale". The default is the worn avatar's
// own scale when it has one. - xlinka
[ComponentCategory("Users")]
public class UserScaleContextActions : ContextMenuItemSource
{
    private const float ScaleTolerance = 0.01f;

    private static readonly float[] EnabledFill = { 0.10f, 0.28f, 0.14f, 0.92f };
    private static readonly float[] DisabledFill = { 0.32f, 0.10f, 0.10f, 0.92f };

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var userRoot = Slot?.ActiveUserRoot;
        if (userRoot == null)
            return;

        // Like locomotion: off a VR hand that holds a tool, always there on desktop.
        if (Engine.Current?.InputInterface?.IsVRActive == true && context?.HandHasTool(userRoot.Slot) == true)
            return;

        float defaultScale = AvatarScale.DefaultFor(userRoot);
        if (MathF.Abs(userRoot.GlobalScale - defaultScale) <= ScaleTolerance)
        {
            var locomotion = userRoot.GetRegisteredComponent<LocomotionController>()
                             ?? userRoot.Slot?.GetComponent<LocomotionController>();
            if (locomotion == null)
                return;

            var toggle = new ContextMenuItem
            {
                Group = ContextMenuGroup.Self,
                IsToggle = true,
                KeepsOpen = true,
                OnPressed = _ =>
                {
                    if (!locomotion.IsDestroyed)
                        locomotion.ScalingEnabled.Value = !locomotion.ScalingEnabled.Value;
                },
                Refresh = item => ShowScaling(item, locomotion),
            };
            ShowScaling(toggle, locomotion);
            page.AddItem(toggle);
            return;
        }

        page.AddItem(new ContextMenuItem
        {
            Group = ContextMenuGroup.Self,
            Label = "Reset Scale",
            FillColor = new[] { 0.32f, 0.19f, 0.10f, 0.92f },
            OnPressed = _ =>
            {
                float before = userRoot.GlobalScale;
                // Through the same clamp the gesture uses. A role whose bounds do not include the default
                // gets the nearest scale it is allowed rather than a reset that snaps straight back.
                var locomotion = userRoot.GetRegisteredComponent<LocomotionController>()
                                 ?? userRoot.Slot?.GetComponent<LocomotionController>();
                float target = locomotion?.ClampUserScale(defaultScale) ?? defaultScale;
                // Feet held, so the reset and its undo land in the same spot.
                var feet = userRoot.FeetPosition;
                userRoot.GlobalScale = target;
                userRoot.ShiftBy(feet - userRoot.FeetPosition);
                var undo = Slot?.GetComponent<UndoManager>()
                           ?? Slot?.ActiveUserRoot?.Slot?.GetComponentInChildren<UndoManager>();
                undo?.Record(new UserScaleUndoBatch(userRoot, before, target));
            },
        });
    }

    // The toggle gates the two-grip pull in VR as well as the desktop binding; the hub says how to pull
    // while it is on.
    private static bool ShowScaling(ContextMenuItem item, LocomotionController locomotion)
    {
        if (locomotion.IsDestroyed)
            return false;
        bool enabled = locomotion.ScalingEnabled.Value;
        string? detail = enabled && Engine.Current?.InputInterface?.IsVRActive == true ? "On, grip both" : null;
        if (item.Label.Length > 0 && item.IsToggled == enabled && item.Detail == detail)
            return false;
        item.IsToggled = enabled;
        item.Label = enabled ? "Scaling Enabled" : "Scaling Disabled";
        item.FillColor = enabled ? EnabledFill : DisabledFill;
        item.Detail = detail;
        return true;
    }
}
