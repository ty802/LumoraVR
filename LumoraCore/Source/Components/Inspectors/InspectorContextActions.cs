// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

/// <summary>
/// Adds "Inspector" to the radial menu: opens a scene inspector rooted at the dev tool's selected
/// slot when one is picked, else at the world root.
/// </summary>
public class InspectorContextActions : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        // Dev tooling only: the item appears while the dev tool is EQUIPPED, not in the default menu,
        // and never in worlds whose mode forbids editing (the hard gate the sync layer also enforces).
        if (World?.AllowsWorldEditing != true)
            return;
        var devTool = Slot?.ActiveUserRoot?.Slot?.GetComponentInChildren<Interaction.DevToolItem>();
        if (devTool == null || !devTool.IsEquipped)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Inspector",
            FillColor = new[] { 0.16f, 0.20f, 0.30f, 0.92f },
            OnPressed = _ => OpenInspector(),
        });
    }

    private void OpenInspector()
    {
        var world = World;
        if (world == null)
            return;

        // Prefer the slot the equipped dev tool currently has selected.
        Slot? target = null;
        var userRoot = Slot?.ActiveUserRoot;
        if (userRoot != null)
        {
            var devTool = userRoot.Slot.GetComponentInChildren<Interaction.DevToolItem>();
            target = devTool?.SelectedSlot.Target;
        }
        target ??= world.RootSlot;

        SceneInspectorPanel.Spawn(world, target);
    }
}
