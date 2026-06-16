// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components;

// Contributes "Undo" / "Redo" to the default context menu. Hidden while the
// summoning hand is holding objects (the held-object actions take that space
// instead). - xlinka
[ComponentCategory("Users")]
public class UndoContextActions : ContextMenuItemSource
{
    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Slot?.ActiveUserRoot?.ActiveUser != World?.LocalUser)
            return;

        var undo = Slot!.GetComponent<UndoManager>()
                   ?? Slot.ActiveUserRoot!.Slot?.GetComponentInChildren<UndoManager>();
        if (undo == null)
            return;

        var side = context?.Side;
        if (side != null)
        {
            foreach (var tool in Slot.ActiveUserRoot!.Slot!.GetComponentsInChildren<HandTool>())
            {
                if (tool.Side.Value == side && tool.IsHoldingObjects)
                    return;
            }
        }

        page.AddItem(new ContextMenuItem
        {
            Label = "Undo",
            IsEnabled = undo.CanUndo,
            FillColor = new[] { 0.32f, 0.10f, 0.10f, 0.92f },
            OnPressed = _ => undo.Undo(),
        });

        page.AddItem(new ContextMenuItem
        {
            Label = "Redo",
            IsEnabled = undo.CanRedo,
            FillColor = new[] { 0.10f, 0.16f, 0.32f, 0.92f },
            OnPressed = _ => undo.Redo(),
        });
    }
}
