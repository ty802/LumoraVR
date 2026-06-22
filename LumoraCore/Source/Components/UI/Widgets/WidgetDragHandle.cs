// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

public class WidgetDragHandle : InteractionElement
{
    public readonly SyncRef<WidgetGrid> Grid;
    public readonly SyncRef<Widget> TargetWidget;

    public WidgetDragHandle()
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

    // Is the cursor pulled clearly below the grid? (the "drag down off the bar" pop-out gesture).
    private static bool IsPopOutGesture(WidgetGrid grid, UIInteractionContext context)
    {
        var gridRect = grid.RectTransform?.LocalComputeRect;
        if (gridRect == null)
            return false;
        return context.LocalPoint.y < gridRect.Value.yMin - grid.EffectiveCellSize.y;
    }

    // During the drag, PREVIEW the destination (a green/red cell highlight) instead of moving
    // the widget. The actual move/pop-out commits on release. -xlinka
    private void OnDragged(UIInteractionContext context)
    {
        var grid = Grid.Target;
        var widget = TargetWidget.Target;
        if (grid == null || widget == null || !grid.EditMode.Value)
            return;

        if (IsPopOutGesture(grid, context))
        {
            grid.ClearDragPreview(); // no cell preview while popping out
            return;
        }

        var (col, row) = grid.CellAt(context.LocalPoint);
        grid.PreviewPlacement(widget, col, row);
    }

    private void OnReleased(UIInteractionContext context)
    {
        var grid = Grid.Target;
        var widget = TargetWidget.Target;
        grid?.ClearDragPreview();
        if (grid == null || widget == null || !grid.EditMode.Value)
            return;

        if (IsPopOutGesture(grid, context))
        {
            grid.PopOut(widget);
            return;
        }

        // Collision-aware: snaps to the cell under the cursor, dodging/shrinking to avoid overlap.
        var (col, row) = grid.CellAt(context.LocalPoint);
        grid.TryPlace(widget, col, row);
    }
}
