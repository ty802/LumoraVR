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

        // Asked here rather than only in the handler so the refusal is something the user can SEE:
        // the radial menu has already closed by the time an action runs, and a button that quietly
        // does nothing reads as a broken button.
        bool canSave = AnySaveableGrabbed(grabber, out var saveRefusal);
        page.AddItem(new ContextMenuItem
        {
            Label = canSave ? "Save to Inventory" : saveRefusal ?? "Save Blocked",
            IsEnabled = canSave,
            FillColor = canSave
                ? new[] { 0.12f, 0.18f, 0.30f, 0.92f }
                : new[] { 0.22f, 0.16f, 0.16f, 0.92f },
            OnPressed = canSave ? _ => SaveGrabbedToInventory(grabber) : null,
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
        var actor = World?.LocalUser;
        foreach (var slot in CollectGrabbedSlots(grabber))
        {
            if (slot.IsDestroyed)
                continue;
            // Game-mechanic props are meant to be played with, not pocketed. Not a security
            // boundary - an inspector still copies whatever it likes - so nothing else in the save
            // path leans on this. - xlinka
            if (GrabSaveBlock.BlocksInventorySave(slot))
            {
                Logging.Logger.Log($"Save to Inventory refused for '{slot.Name}': marked not saveable.");
                continue;
            }
            // This one IS the boundary. Holding something is not owning it: a grab parents the object
            // under your hand, which reads as ownership everywhere else, so the copy question has to be
            // asked of the gate rather than of where the thing currently hangs. -xlinka
            if (!ItemProtection.AllowsSaveCopy(slot, actor, out var reason))
            {
                Logging.Logger.Log($"Save to Inventory refused for '{slot.Name}': {reason ?? "not allowed"}.");
                continue;
            }
            // Packed here on the world thread, uploaded off it; the answer only goes to the log because
            // the radial menu that asked is already gone. Lands at the inventory root. -xlinka
            var name = slot.Name;
            _ = Inventory.SaveItemAsync(slot, name, null).ContinueWith(t =>
            {
                var result = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                    ? t.Result
                    : InventoryResult.Failure(t.Exception?.GetBaseException().Message ?? "upload failed");
                Logging.Logger.Log($"Save to Inventory '{name}': {result.Message}");
            });
        }
    }

    // Whether anything in the hand can actually be filed away, and a short label for why not when
    // nothing can. A mixed hand stays enabled and the handler skips the pieces it may not take.
    private bool AnySaveableGrabbed(Grabber grabber, out string? refusal)
    {
        refusal = null;
        var actor = World?.LocalUser;
        bool marked = false;
        bool denied = false;

        foreach (var slot in CollectGrabbedSlots(grabber))
        {
            if (slot.IsDestroyed)
                continue;
            if (GrabSaveBlock.BlocksInventorySave(slot))
            {
                marked = true;
                continue;
            }
            if (!ItemProtection.AllowsSaveCopy(slot, actor, out _))
            {
                denied = true;
                continue;
            }
            return true;
        }

        refusal = denied ? "Protected" : marked ? "Not Saveable" : null;
        return false;
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

        var batch = SlotExistenceUndoBatch.Created(World, duplicates, UndoLocale.Duplicate);
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
