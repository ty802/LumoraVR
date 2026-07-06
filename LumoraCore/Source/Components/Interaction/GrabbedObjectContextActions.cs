// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components.Interaction;

// Contributes "Destroy" and "Duplicate" when the hand that summoned the menu
// is holding objects. - xlinka
[ComponentCategory("Interaction")]
public class GrabbedObjectContextActions : ContextMenuItemSource
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

        var grabber = hand?.Grabber;
        if (grabber == null || !grabber.IsHoldingObjects)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Destroy",
            FillColor = new[] { 0.32f, 0.10f, 0.10f, 0.92f },
            OnPressed = _ => DestroyGrabbed(grabber),
        });

        page.AddItem(new ContextMenuItem
        {
            Label = "Duplicate",
            FillColor = new[] { 0.10f, 0.28f, 0.14f, 0.92f },
            OnPressed = _ => DuplicateGrabbed(grabber),
        });

        page.AddItem(new ContextMenuItem
        {
            Label = "Save to Inventory",
            FillColor = new[] { 0.12f, 0.18f, 0.30f, 0.92f },
            OnPressed = _ => SaveGrabbedToInventory(grabber),
        });

        // Equip on avatar: if a held object is an (unworn) avatar, offer to wear it from the held-object menu.
        // Releases the grab, then routes through the body-node dispatch. - xlinka
        var manager = Slot?.ActiveUserRoot?.GetRegisteredComponent<AvatarEquipManager>();
        if (manager != null)
        {
            foreach (var slot in CollectGrabbedSlots(grabber))
            {
                var avatarRoot = slot.GetComponent<AvatarForm>();
                if (avatarRoot == null || avatarRoot.IsEquipped || slot == manager.CurrentAvatar.Target)
                    continue;

                var targetSlot = slot;
                page.AddItem(new ContextMenuItem
                {
                    Label = "Equip Avatar",
                    FillColor = new[] { 0.14f, 0.30f, 0.18f, 0.92f },
                    OnPressed = _ =>
                    {
                        grabber.ReleaseAll();
                        manager.EquipAvatar(targetSlot);
                    },
                });
                break; // one equip item even if multiple avatars are held
            }
        }
    }

    private void SaveGrabbedToInventory(Grabber grabber)
    {
        foreach (var slot in CollectGrabbedSlots(grabber))
        {
            if (!slot.IsDestroyed)
                Inventory.SaveItem(slot, slot.Name);
        }
    }

    private void DestroyGrabbed(Grabber grabber)
    {
        var slots = CollectGrabbedSlots(grabber);
        grabber.ReleaseAll();

        // Undoable destroy: the batch parks the slots in the graveyard and only
        // destroys for real once it leaves the history.
        var batch = SlotExistenceUndoBatch.Destroy(World, slots);
        if (batch != null)
        {
            FindUndoManager()?.Record(batch);
            return;
        }

        foreach (var slot in slots)
        {
            if (!slot.IsDestroyed)
                slot.Destroy();
        }
    }

    private void DuplicateGrabbed(Grabber grabber)
    {
        var root = World?.RootSlot;
        if (root == null)
            return;

        var duplicates = new List<Slot>();
        foreach (var slot in CollectGrabbedSlots(grabber))
        {
            if (slot.IsDestroyed)
                continue;
            var copy = slot.Duplicate(root, preserveGlobalTransform: true);
            if (copy != null)
                duplicates.Add(copy);
        }

        var batch = SlotExistenceUndoBatch.Created(World, duplicates, "Duplicate");
        if (batch != null)
            FindUndoManager()?.Record(batch);
    }

    private UndoManager? FindUndoManager()
    {
        return Slot?.GetComponent<UndoManager>()
               ?? Slot?.ActiveUserRoot?.Slot?.GetComponentInChildren<UndoManager>();
    }

    private static List<Slot> CollectGrabbedSlots(Grabber grabber)
    {
        var slots = new List<Slot>();
        foreach (var grabbable in grabber.GrabbedObjects)
        {
            var slot = (grabbable as Component)?.Slot;
            if (slot != null && !slot.IsDestroyed)
                slots.Add(slot);
        }
        return slots;
    }
}
