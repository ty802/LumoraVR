// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Interaction;

// Contributes the "Unequip Tool" action when the hand that summoned the menu
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

        if (hand == null)
            return;

        var item = hand.ActiveToolItem.Target;
        var grabber = hand.Grabber;
        bool holdingSomething = grabber != null && grabber.IsHoldingObjects;
        if ((item == null || item.IsDestroyed) && !holdingSomething)
            return;

        // Equip and unequip stay on the ROOT ring. They are the one thing you always want reachable
        // with a tool in hand, and burying them one level down under "Tool Actions" next to the mode
        // switches meant two clicks to put a tool away. The per-tool actions still nest. -xlinka
        if (item != null && !item.IsDestroyed)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Unequip Tool",
                FillColor = new[] { 0.30f, 0.22f, 0.12f, 0.92f },
                OnPressed = _ => hand.EquipToolItem(null),
            });
        }

        // Holding a tool: offer to equip it into this hand. Allowed even with a tool already equipped (the
        // equip swaps, popping the old one off) - the hand carries a default tool, so a held-only gate would
        // make this item unreachable. Release the grab first or the grabber keeps a ref to a docked slot. -xlinka
        if (grabber == null || !grabber.IsHoldingObjects)
            return;

        foreach (var grabbable in grabber.GrabbedObjects)
        {
            var slot = (grabbable as Component)?.Slot;
            var heldItem = slot?.GetComponent<ToolItem>();
            if (heldItem == null || heldItem.IsDestroyed || heldItem.IsEquipped || ReferenceEquals(heldItem, item))
                continue;

            var equipTarget = heldItem;
            var heldGrabbable = grabbable;
            page.AddItem(new ContextMenuItem
            {
                Label = "Equip Tool",
                FillColor = new[] { 0.14f, 0.30f, 0.18f, 0.92f },
                OnPressed = _ =>
                {
                    grabber.Release(heldGrabbable);
                    hand.EquipToolItem(equipTarget);
                },
            });
            break; // one equip item even if several tools are held
        }
    }
}
