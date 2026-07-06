// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

/// <summary>
/// Flows children left-to-right, wrapping to a new row when the next child would
/// overflow the available width (CSS flex-wrap). Each row's height is its tallest
/// child; rows stack top-to-bottom. Children size to their preferred metrics,
/// clamped to the container width.
/// </summary>
public class WrapLayout : LayoutController
{
    public readonly Sync<float> Spacing;       // horizontal gap between items in a row
    public readonly Sync<float> LineSpacing;   // vertical gap between rows
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;
    // Cross-axis (vertical) alignment of a child within its row's height.
    public readonly Sync<LayoutAlignment> RowAlignment;

    private float _spacing;
    private float _lineSpacing;
    private float _padL;
    private float _padR;
    private float _padT;
    private float _padB;
    private LayoutAlignment _rowAlign;

    private readonly List<float> _w = new();
    private readonly List<float> _h = new();

    public WrapLayout()
    {
        Spacing = new Sync<float>(this, 0f);
        LineSpacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
        RowAlignment = new Sync<LayoutAlignment>(this, LayoutAlignment.Center);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkInvalidateHorizontalLayout();
        rect.MarkInvalidateVerticalLayout();
    }

    public override void PrepareCompute()
    {
        _spacing = Spacing.Value;
        _lineSpacing = LineSpacing.Value;
        _padL = PaddingLeft.Value;
        _padR = PaddingRight.Value;
        _padT = PaddingTop.Value;
        _padB = PaddingBottom.Value;
        _rowAlign = RowAlignment.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float innerW = rect.width - _padL - _padR;
        if (innerW < 0f) innerW = 0f;

        MeasureChildren(children, innerW);

        float xStart = rect.xMin + _padL;
        float rowTop = rect.yMax - _padT;
        float alignT = _rowAlign switch { LayoutAlignment.Start => 0f, LayoutAlignment.End => 1f, _ => 0.5f };

        int i = 0;
        while (i < children.Count)
        {
            // Gather the next row: as many children as fit, but always at least one.
            int j = i;
            float rowW = 0f;
            float rowH = 0f;
            int placed = 0;
            while (j < children.Count)
            {
                if (LayoutSizing.IsIgnored(children[j])) { j++; continue; }
                float add = (placed > 0 ? _spacing : 0f) + _w[j];
                if (placed > 0 && rowW + add > innerW) break;
                rowW += add;
                if (_h[j] > rowH) rowH = _h[j];
                placed++;
                j++;
            }

            if (placed == 0) { i = j; continue; } // only ignored remained

            float penX = xStart;
            bool first = true;
            for (int k = i; k < j; k++)
            {
                if (LayoutSizing.IsIgnored(children[k])) continue;
                if (!first) penX += _spacing;
                float yBottom = rowTop - _h[k] - (rowH - _h[k]) * alignT;
                children[k].SetLocalComputeRect(new Rect(penX, yBottom, _w[k], _h[k]));
                penX += _w[k];
                first = false;
            }

            rowTop -= rowH + _lineSpacing;
            i = j;
        }
    }

    public override void EnsureValidMetrics(LayoutDirection direction)
    {
        if (RectTransform == null) return;

        var children = RectTransform.RectChildren;

        if (direction == LayoutDirection.Horizontal)
        {
            float maxChild = 0f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < children.Count; i++)
            {
                if (LayoutSizing.IsIgnored(children[i])) continue;
                var m = LayoutSizing.Measured(children[i], LayoutDirection.Horizontal);
                float cw = m.Preferred > 0f ? m.Preferred : m.Min;
                if (cw > maxChild) maxChild = cw;
                sum += cw;
                n++;
            }
            sum += _spacing * Max(0, n - 1);
            _minWidth = maxChild + _padL + _padR;      // narrowest: the widest single child
            _preferredWidth = sum + _padL + _padR;     // widest: everything on one row
            _flexibleWidth = 0f;
            return;
        }

        // Vertical height depends on the wrap at the current width.
        float innerW = RectTransform.LocalComputeRect.width - _padL - _padR;
        if (innerW < 0f) innerW = 0f;
        MeasureChildren(children, innerW);

        float total = 0f;
        int rows = 0;
        int idx = 0;
        while (idx < children.Count)
        {
            int j = idx;
            float rowW = 0f;
            float rowH = 0f;
            int placed = 0;
            while (j < children.Count)
            {
                if (LayoutSizing.IsIgnored(children[j])) { j++; continue; }
                float add = (placed > 0 ? _spacing : 0f) + _w[j];
                if (placed > 0 && rowW + add > innerW) break;
                rowW += add;
                if (_h[j] > rowH) rowH = _h[j];
                placed++;
                j++;
            }
            if (placed == 0) { idx = j; continue; }
            total += rowH;
            rows++;
            idx = j;
        }
        total += _lineSpacing * Max(0, rows - 1);

        _minHeight = total + _padT + _padB;
        _preferredHeight = _minHeight;
        _flexibleHeight = 0f;
    }

    private void MeasureChildren(IReadOnlyList<RectTransform> children, float innerW)
    {
        _w.Clear();
        _h.Clear();
        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i]))
            {
                _w.Add(0f);
                _h.Add(0f);
                continue;
            }
            var mw = LayoutSizing.Measured(children[i], LayoutDirection.Horizontal);
            var mh = LayoutSizing.Measured(children[i], LayoutDirection.Vertical);
            float cw = mw.Preferred > 0f ? mw.Preferred : mw.Min;
            if (innerW > 0f && cw > innerW) cw = innerW;
            float ch = mh.Preferred > 0f ? mh.Preferred : mh.Min;
            _w.Add(cw);
            _h.Add(ch);
        }
    }

    private static float Max(float a, float b) => a > b ? a : b;
    private static int Max(int a, int b) => a > b ? a : b;
}
