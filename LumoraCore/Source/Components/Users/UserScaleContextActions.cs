// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// Scale items for the default menu: at default scale a toggle for the user's
// scaling permission, otherwise "Reset Scale". - xlinka
[ComponentCategory("Users")]
public class UserScaleContextActions : ContextMenuItemSource
{
    private const float DefaultScale = 1f;
    private const float ScaleTolerance = 0.01f;

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var userRoot = Slot?.ActiveUserRoot;
        if (userRoot == null)
            return;

        if (MathF.Abs(userRoot.GlobalScale - DefaultScale) <= ScaleTolerance)
        {
            var locomotion = userRoot.GetRegisteredComponent<LocomotionController>()
                             ?? userRoot.Slot?.GetComponent<LocomotionController>();
            if (locomotion == null)
                return;

            bool enabled = locomotion.ScalingEnabled.Value;
            page.AddItem(new ContextMenuItem
            {
                Label = enabled ? "Scaling Enabled" : "Scaling Disabled",
                IsToggle = true,
                IsToggled = enabled,
                FillColor = enabled
                    ? new[] { 0.10f, 0.28f, 0.14f, 0.92f }
                    : new[] { 0.32f, 0.10f, 0.10f, 0.92f },
                OnPressed = _ => locomotion.ScalingEnabled.Value = !locomotion.ScalingEnabled.Value,
            });
            return;
        }

        page.AddItem(new ContextMenuItem
        {
            Label = "Reset Scale",
            FillColor = new[] { 0.32f, 0.19f, 0.10f, 0.92f },
            OnPressed = _ =>
            {
                float before = userRoot.GlobalScale;
                userRoot.GlobalScale = DefaultScale;
                var undo = Slot?.GetComponent<UndoManager>()
                           ?? Slot?.ActiveUserRoot?.Slot?.GetComponentInChildren<UndoManager>();
                undo?.Record(new UserScaleUndoBatch(userRoot, before, DefaultScale));
            },
        });
    }
}
