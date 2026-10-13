// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.UI;
using Lumora.Core.Localization;

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

        // Both stay up after a press, so stepping back through several changes is one press each.
        var undoItem = new ContextMenuItem
        {
            Group = ContextMenuGroup.History,
            FillColor = new[] { 0.32f, 0.10f, 0.10f, 0.92f },
            KeepsOpen = true,
            OnPressed = _ => undo.Undo(),
            Refresh = item => Show(item, undo, undoDirection: true),
        };
        Show(undoItem, undo, undoDirection: true);
        page.AddItem(undoItem);

        var redoItem = new ContextMenuItem
        {
            Group = ContextMenuGroup.History,
            FillColor = new[] { 0.10f, 0.16f, 0.32f, 0.92f },
            KeepsOpen = true,
            OnPressed = _ => undo.Redo(),
            Refresh = item => Show(item, undo, undoDirection: false),
        };
        Show(redoItem, undo, undoDirection: false);
        page.AddItem(redoItem);
    }

    // The next step's name and whether there is one, as the manager has them now; true when either moved.
    private static bool Show(ContextMenuItem item, UndoManager undo, bool undoDirection)
    {
        if (undo.IsDestroyed)
            return false;
        bool can = undoDirection ? undo.CanUndo : undo.CanRedo;
        string label = undoDirection
            ? Label(UndoLocale.Undo, can ? undo.NextUndoDescription : LocaleText.Empty, undoDirection: true)
            : Label(UndoLocale.Redo, can ? undo.NextRedoDescription : LocaleText.Empty, undoDirection: false);
        if (item.IsEnabled == can && item.Label == label)
            return false;
        item.IsEnabled = can;
        item.Label = label;
        return true;
    }

    // A radial slice is narrow, so the step name only earns its place when it is short enough to
    // still read at a glance; past that the bare verb is more use than a clipped sentence.
    private const int MaxStepNameLength = 18;

    private static string Label(LocaleText verb, LocaleText step, bool undoDirection)
    {
        if (step.IsEmpty)
            return verb.Resolve();
        string name = step.Resolve();
        if (name.Length == 0 || name.Length > MaxStepNameLength)
            return verb.Resolve();
        return (undoDirection ? UndoLocale.UndoNamed(name) : UndoLocale.RedoNamed(name)).Resolve();
    }
}
