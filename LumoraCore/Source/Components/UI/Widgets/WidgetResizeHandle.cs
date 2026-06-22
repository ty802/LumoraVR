// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

// A bottom-right grip: drag it to resize its widget in the grid. The cell under the cursor becomes the
// widget's new bottom-right corner, collision-aware (shrinks to fit, never overlaps a neighbour). The
// resize redefines the widget's authored footprint so reflow remembers it. Edit-mode only. -xlinka
public class WidgetResizeHandle : InteractionElement
{
    public readonly SyncRef<WidgetGrid> Grid;
    public readonly SyncRef<Widget> TargetWidget;

    public WidgetResizeHandle()
    {
        Grid = new SyncRef<WidgetGrid>(this);
        TargetWidget = new SyncRef<Widget>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        Dragged += OnDragged;
        Released += OnReleased;
    }

    // Preview the new size (green/red destination highlight) during the drag; commit on release. -xlinka
    private void OnDragged(UIInteractionContext context)
    {
        var grid = Grid.Target;
        var widget = TargetWidget.Target;
        if (grid == null || widget == null || !grid.EditMode.Value)
            return;

        var (col, row) = grid.CellAt(context.LocalPoint);
        grid.PreviewResize(widget, col, row);
    }

    private void OnReleased(UIInteractionContext context)
    {
        var grid = Grid.Target;
        var widget = TargetWidget.Target;
        grid?.ClearDragPreview();
        if (grid == null || widget == null || !grid.EditMode.Value)
            return;

        // The cell under the cursor is the widget's new bottom-right corner.
        var (col, row) = grid.CellAt(context.LocalPoint);
        grid.ResizeTo(widget, col, row);
    }
}
