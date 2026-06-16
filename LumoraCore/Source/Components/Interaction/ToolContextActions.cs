// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Interaction;

// Contributes the "Dequip Tool" action when the hand that summoned the menu
// has a tool item equipped. - xlinka
[ComponentCategory("Interaction")]
public class ToolContextActions : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var side = context?.Side;
        if (side == null)
            return;

        HandTool? hand = null;
        foreach (var tool in Slot!.ActiveUserRoot!.Slot.GetComponentsInChildren<HandTool>())
        {
            if (tool.Side.Value == side)
            {
                hand = tool;
                break;
            }
        }

        var item = hand?.ActiveToolItem.Target;
        if (item == null || item.IsDestroyed)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Dequip Tool",
            FillColor = new[] { 0.30f, 0.22f, 0.12f, 0.92f },
            OnPressed = _ => hand!.EquipToolItem(null),
        });
    }
}
