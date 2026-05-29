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
    }

    private void OnDragged(UIInteractionContext context)
    {
        var grid = Grid.Target;
        var widget = TargetWidget.Target;
        if (grid == null || widget == null || !grid.EditMode.Value)
            return;

        var gridRect = grid.RectTransform?.LocalComputeRect;
        if (gridRect == null)
            return;

        var cell = grid.CellSize.Value;
        var spacing = grid.Spacing.Value;
        var padding = grid.Padding.Value;

        float gx = context.LocalPoint.x - gridRect.Value.xMin - padding.x;
        float gy = gridRect.Value.yMax - context.LocalPoint.y - padding.y;

        int cellX = (int)MathF.Floor(gx / (cell.x + spacing.x));
        int cellY = (int)MathF.Floor(gy / (cell.y + spacing.y));

        widget.GridX.Value = cellX < 0 ? 0 : cellX;
        widget.GridY.Value = cellY < 0 ? 0 : cellY;
    }
}
