// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// Contributes the locomotion module picker to the context menu — one toggle
// item per registered module, current one highlighted. Runs on the peer
// opening the menu; activation goes through the controller's permission
// check. - xlinka
[ComponentCategory("Locomotion")]
public class LocomotionContextActions : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        // Locomotion is the local user's own concern.
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var locomotion = Slot?.ActiveUserRoot?.GetRegisteredComponent<LocomotionController>()
                         ?? Slot?.ActiveUserRoot?.Slot?.GetComponent<LocomotionController>();
        if (locomotion == null || locomotion.Modules.Count == 0)
            return;

        // Modules live one level down: root shows a single "Locomotion"
        // entry that opens the picker sub-page.
        var subPage = new ContextMenuPage("Locomotion");
        foreach (var module in locomotion.Modules)
        {
            if (module == null || module.IsDestroyed)
                continue;
            if (string.IsNullOrEmpty(module.DisplayName) || module.DisplayName == "None")
                continue;

            bool isActive = ReferenceEquals(locomotion.ActiveModule, module);
            var captured = module;
            subPage.AddItem(new ContextMenuItem
            {
                Label = module.DisplayName,
                IsToggle = true,
                IsToggled = isActive,
                FillColor = new[] { 0.13f, 0.16f, 0.24f, 0.92f },
                OnPressed = _ => locomotion.ActivateModule(captured),
            });
        }

        if (subPage.Items.Count == 0)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Locomotion",
            FillColor = new[] { 0.13f, 0.16f, 0.24f, 0.92f },
            SubPage = subPage,
        });
    }
}
