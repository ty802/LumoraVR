// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Gizmos;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Interaction;

// Lives on the tool item's slot, so the menu only collects it while the tool is actually in a hand.
// Contributes into the SAME "Tool Actions" submenu the gizmo modes and equip/dequip use - a second
// root entry per tool would put the radial ring back where it was before the submenu existed.
[ComponentCategory("Interaction/Tools")]
public sealed class ToolActionsMenuSource : ContextMenuItemSource
{
    public readonly SyncRef<EquippableTool> Tool;

    public ToolActionsMenuSource()
    {
        Tool = new SyncRef<EquippableTool>(this);
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        var tool = Tool.Target;
        if (tool == null || tool.IsDestroyed || !tool.Enabled.Value)
            return;
        // A tool lying in the world is not this hand's business; only the equipped one gets a say.
        if (!tool.IsEquipped)
            return;

        tool.PopulateToolActions(
            page.GetOrAddSubPage(GizmoModeMenuSource.ToolSubmenuLabel, GizmoModeMenuSource.ToolSubmenuFill),
            context);
    }
}
