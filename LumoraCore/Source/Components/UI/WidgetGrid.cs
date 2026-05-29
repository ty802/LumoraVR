// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class WidgetGrid : UIComponent
{
    public readonly Sync<float2> CellSize;
    public readonly Sync<float2> Spacing;
    public readonly Sync<float2> Padding;
    public readonly Sync<bool> EditMode;

    public WidgetGrid()
    {
        CellSize = new Sync<float2>(this, new float2(120f, 90f));
        Spacing = new Sync<float2>(this, new float2(8f, 8f));
        Padding = new Sync<float2>(this, new float2(8f, 8f));
        EditMode = new Sync<bool>(this, false);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        ArrangeWidgets();
    }

    public void ArrangeWidgets()
    {
        var cell = CellSize.Value;
        var spacing = Spacing.Value;
        var padding = Padding.Value;
        bool edit = EditMode.Value;

        var widgets = new List<Widget>(Slot.GetComponentsInChildren<Widget>(false));
        foreach (var widget in widgets)
        {
            var rect = widget.RectTransform ?? widget.Slot.GetComponent<RectTransform>() ?? widget.Slot.AttachComponent<RectTransform>();
            int gridWidth = Max(1, widget.GridWidth.Value);
            int gridHeight = Max(1, widget.GridHeight.Value);
            float width = gridWidth * cell.x + (gridWidth - 1) * spacing.x;
            float height = gridHeight * cell.y + (gridHeight - 1) * spacing.y;
            float xMin = padding.x + widget.GridX.Value * (cell.x + spacing.x);
            float yMax = -(padding.y + widget.GridY.Value * (cell.y + spacing.y));

            rect.AnchorMin.Value = new float2(0f, 1f);
            rect.AnchorMax.Value = new float2(0f, 1f);
            rect.OffsetMin.Value = new float2(xMin, yMax - height);
            rect.OffsetMax.Value = new float2(xMin + width, yMax);

            var handle = widget.Slot.GetComponent<WidgetDragHandle>() ?? widget.Slot.AttachComponent<WidgetDragHandle>();
            handle.Grid.Target = this;
            handle.TargetWidget.Target = widget;
            handle.Interactable.Value = edit;
        }
    }

    private static int Max(int a, int b) => a > b ? a : b;
}
