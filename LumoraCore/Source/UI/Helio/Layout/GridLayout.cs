// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// fixed-column grid. cell size derived from layout rect / column count.
// TODO - xlinka: explicit CellSize + auto-column-count + per-child priority once metrics solver lands
public class GridLayout : LayoutController
{
    public readonly Sync<int> Columns;
    public readonly Sync<float> Spacing;
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;

    private int _columns;
    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;

    public GridLayout()
    {
        Columns = new Sync<int>(this, 3);
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
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
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        int cols = _columns;
        int rows = (children.Count + cols - 1) / cols;

        float innerWidth = rect.width - _padLeft - _padRight - _spacing * (cols - 1);
        float innerHeight = rect.height - _padTop - _padBottom - _spacing * (rows - 1);
        if (innerWidth < 0f) innerWidth = 0f;
        if (innerHeight < 0f) innerHeight = 0f;

        float cellW = innerWidth / cols;
        float cellH = innerHeight / rows;

        float xStart = rect.xMin + _padLeft;
        float yTop = rect.yMax - _padTop;

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            int col = i % cols;
            int row = i / cols;
            float x = xStart + col * (cellW + _spacing);
            float yBottom = yTop - row * (cellH + _spacing) - cellH;
            children[i].SetLocalComputeRect(new Rect(x, yBottom, cellW, cellH));
        }
    }
}
