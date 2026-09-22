// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// Lays children out on a grid of fixed cells.
[ComponentCategory("Utility/Transforms")]
public class ObjectGridAligner : ChildLayoutBase
{
    public readonly Sync<int> ItemsPerRow;

    public readonly Sync<float2> CellSize;

    public readonly Sync<LayoutAxis> RowAxis;

    public readonly Sync<LayoutAxis> ColumnAxis;

    public readonly Sync<LayoutAlign> RowAlignment;

    public readonly Sync<LayoutAlign> ColumnAlignment;

    public ObjectGridAligner()
    {
        ItemsPerRow = new Sync<int>(this, 4);
        CellSize = new Sync<float2>(this, new float2(0.25f, 0.25f));
        RowAxis = new Sync<LayoutAxis>(this, LayoutAxis.XPositive);
        ColumnAxis = new Sync<LayoutAxis>(this, LayoutAxis.ZPositive);
        RowAlignment = new Sync<LayoutAlign>(this, LayoutAlign.Center);
        ColumnAlignment = new Sync<LayoutAlign>(this, LayoutAlign.Positive);
    }

    public override void OnUpdate(float delta)
    {
        var items = OrderedItems;
        int count = items.Count;
        if (count == 0)
            return;

        int perRow = System.Math.Max(1, ItemsPerRow.Value);
        int columns = System.Math.Min(count, perRow);
        int rows = (count + perRow - 1) / perRow;
        var cell = CellSize.Value;

        float rowStart = AlignmentOffset(RowAlignment.Value, columns * cell.x, cell.x);
        float columnStart = AlignmentOffset(ColumnAlignment.Value, rows * cell.y, cell.y);

        var rowAxis = RowAxis.Value;
        var columnAxis = ColumnAxis.Value;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (!item.Position.IsLinkValid)
                continue;
            int column = i % perRow;
            int row = i / perRow;
            var position = AxisVector(rowAxis, cell.x * column + rowStart)
                + AxisVector(columnAxis, cell.y * row + columnStart);
            Push(item.Position, position);
        }
    }

    private static float AlignmentOffset(LayoutAlign align, float total, float cell) => align switch
    {
        LayoutAlign.Positive => cell * 0.5f,
        LayoutAlign.Center => total * -0.5f + cell * 0.5f,
        _ => -total + cell * 0.5f,
    };
}
