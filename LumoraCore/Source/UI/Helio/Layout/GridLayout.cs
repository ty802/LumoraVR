// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// Grid layout. Two modes:
//  - Fixed column count (default): cell size = layout rect / column count (cells stretch to fill width).
//  - Fixed cell size (set CellSize.x > 0): cells are a fixed size and the column count is derived from
//    the available width, wrapping top-left - the right choice for card grids that shouldn't stretch.
public class GridLayout : LayoutController
{
    public readonly Sync<int> Columns;
    public readonly Sync<float> Spacing;
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;

    /// <summary>Fixed cell size in canvas units. x > 0 switches to fixed-cell mode (y <= 0 = square).</summary>
    public readonly Sync<float2> CellSize;

    private int _columns;
    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private float2 _cellSize;

    public GridLayout()
    {
        Columns = new Sync<int>(this, 3);
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
        CellSize = new Sync<float2>(this, float2.Zero);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _columns = Math.Max(1, Columns.Value);
        _spacing = Spacing.Value;
        _padLeft = PaddingLeft.Value;
        _padRight = PaddingRight.Value;
        _padTop = PaddingTop.Value;
        _padBottom = PaddingBottom.Value;
        _cellSize = CellSize.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float xStart = rect.xMin + _padLeft;
        float yTop = rect.yMax - _padTop;

        // Fixed-cell mode: cells keep their size and wrap based on available width (card grid).
        if (_cellSize.x > 0f)
        {
            float cellW = _cellSize.x;
            float cellH = _cellSize.y > 0f ? _cellSize.y : cellW;
            float available = rect.width - _padLeft - _padRight;
            int cols = Math.Max(1, (int)((available + _spacing) / (cellW + _spacing)));

            for (int i = 0; i < children.Count; i++)
            {
                if (LayoutSizing.IsIgnored(children[i])) continue;
                int col = i % cols;
                int row = i / cols;
                float x = xStart + col * (cellW + _spacing);
                float yBottom = yTop - row * (cellH + _spacing) - cellH;
                children[i].SetLocalComputeRect(new Rect(x, yBottom, cellW, cellH));
            }
            return;
        }

        // Fixed-column mode: cells divide the rect evenly.
        int columns = _columns;
        int rows = (children.Count + columns - 1) / columns;

        float innerWidth = rect.width - _padLeft - _padRight - _spacing * (columns - 1);
        float innerHeight = rect.height - _padTop - _padBottom - _spacing * (rows - 1);
        if (innerWidth < 0f) innerWidth = 0f;
        if (innerHeight < 0f) innerHeight = 0f;

        float colW = innerWidth / columns;
        float rowH = innerHeight / rows;

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            int col = i % columns;
            int row = i / columns;
            float x = xStart + col * (colW + _spacing);
            float yBottom = yTop - row * (rowH + _spacing) - rowH;
            children[i].SetLocalComputeRect(new Rect(x, yBottom, colW, rowH));
        }
    }
}
