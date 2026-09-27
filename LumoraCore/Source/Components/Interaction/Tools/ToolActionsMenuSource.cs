// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Interaction;

// Lives on the tool item's slot, so the menu only collects it while the tool is in a hand. The tool's own
// actions go straight onto the root ring of the hand holding it, after the equip and undo items. -xlinka
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
        // A tool lying in the world is not this hand's business, and a tool in the other hand is that
        // hand's; only the one equipped in the summoning hand gets a say.
        if (!tool.IsEquipped)
            return;
        if (context?.Side != null && tool.ActiveTool is HandTool hand && hand.Side.Value != context.Side)
            return;

        int first = page.Items.Count;
        tool.PopulateToolActions(page, context!);
        for (int i = first; i < page.Items.Count; i++)
            page.Items[i].Group = ContextMenuGroup.Tool;
    }
}
