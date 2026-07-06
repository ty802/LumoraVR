// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// arranges children top-to-bottom within the layout's rect.
public class VerticalLayout : LayoutController
{
    public readonly Sync<float> Spacing;
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;
    public readonly Sync<bool> ForceExpandWidth;
    public readonly Sync<bool> ForceExpandHeight;
    // Where children sit on the cross (horizontal) axis when not force-expanded. Default Center = prior behavior.
    public readonly Sync<LayoutAlignment> CrossAlignment;
    // How children distribute along the main (vertical) axis when not force-expanded (justify-content).
    public readonly Sync<MainAxisAlignment> MainAlignment;

    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private bool _forceExpandWidth;
    private bool _forceExpandHeight;
    private LayoutAlignment _crossAlignment;
    private MainAxisAlignment _mainAlignment;
    private readonly List<LayoutMetrics> _metrics = new();
    private readonly List<LayoutSizing.Element> _elements = new();

    public VerticalLayout()
    {
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
        ForceExpandWidth = new Sync<bool>(this, true);
        ForceExpandHeight = new Sync<bool>(this, true);
        CrossAlignment = new Sync<LayoutAlignment>(this, LayoutAlignment.Center);
        MainAlignment = new Sync<MainAxisAlignment>(this, MainAxisAlignment.Start);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        // A VerticalLayout also sizes children on the cross (horizontal) axis (PaddingLeft/Right,
        // ForceExpandWidth, CrossAlignment), so a change invalidates BOTH axes' metrics. -xlinka
        rect.MarkInvalidateVerticalLayout();
        rect.MarkInvalidateHorizontalLayout();
    }

    public override void PrepareCompute()
    {
        _spacing = Spacing.Value;
        _padLeft = PaddingLeft.Value;
        _padRight = PaddingRight.Value;
        _padTop = PaddingTop.Value;
        _padBottom = PaddingBottom.Value;
        _forceExpandWidth = ForceExpandWidth.Value;
        _forceExpandHeight = ForceExpandHeight.Value;
        _crossAlignment = CrossAlignment.Value;
        _mainAlignment = MainAlignment.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float innerHeight = rect.height - _padTop - _padBottom;
        if (innerHeight < 0f) innerHeight = 0f;

        float xMin = rect.xMin + _padLeft;
        float xMax = rect.xMax - _padRight;
        float availableWidth = xMax - xMin;
        if (availableWidth < 0f) availableWidth = 0f;

        _metrics.Clear();
        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i]))
            {
                _metrics.Add(default);
                continue;
            }
            var m = LayoutSizing.Measured(children[i], LayoutDirection.Vertical);
            var margin = LayoutSizing.GetMargin(children[i]);
            float mainMargin = margin.y + margin.w; // bottom + top
            m.Min += mainMargin;
            m.Preferred += mainMargin;
            _metrics.Add(m);
        }

        LayoutSizing.Distribute(innerHeight, _spacing, _padTop, _metrics, _elements, _forceExpandHeight);

        if (!_forceExpandHeight)
            LayoutSizing.AlignMainAxis(_elements, innerHeight, _padTop, _spacing, _mainAlignment);

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            var element = _elements[i];
            var margin = LayoutSizing.GetMargin(children[i]);
            float mainMargin = margin.y + margin.w;  // bottom + top
            float crossMargin = margin.x + margin.z; // left + right

            float cellWidth = availableWidth - crossMargin;
            if (cellWidth < 0f) cellWidth = 0f;
            float childWidth = cellWidth;
            float childX = xMin + margin.x;

            if (!_forceExpandWidth)
            {
                var horizontal = LayoutSizing.Measured(children[i], LayoutDirection.Horizontal);
                childWidth = Clamp(horizontal.Flexible > 0f ? cellWidth : Max(horizontal.Min, Min(horizontal.Preferred, cellWidth)), 0f, cellWidth);
                // Cross-axis (horizontal): Start=left, End=right, Center=middle.
                float align = _crossAlignment switch { LayoutAlignment.Start => 0f, LayoutAlignment.End => 1f, _ => 0.5f };
                childX = xMin + margin.x + (cellWidth - childWidth) * align;
            }

            float childHeight = element.Size - mainMargin;
            if (childHeight < 0f) childHeight = 0f;
            // element.Offset/Size include the main-axis margins; inset by the bottom margin.
            float yBottom = rect.yMax - element.Offset - element.Size + margin.y;
            children[i].SetLocalComputeRect(new Rect(childX, yBottom, childWidth, childHeight));
        }
    }

    public override void EnsureValidMetrics(LayoutDirection direction)
    {
        if (RectTransform == null) return;

        float min = 0f;
        float preferred = 0f;
        float flexible = 0f;
        int count = RectTransform.RectChildren.Count;

        for (int i = 0; i < count; i++)
        {
            if (LayoutSizing.IsIgnored(RectTransform.RectChildren[i])) continue;
            var metrics = LayoutSizing.Measured(RectTransform.RectChildren[i], direction);
            var margin = LayoutSizing.GetMargin(RectTransform.RectChildren[i]);
            if (direction == LayoutDirection.Vertical)
            {
                float mm = margin.y + margin.w;
                min += metrics.Min + mm;
                preferred += metrics.Preferred + mm;
                flexible += metrics.Flexible;
            }
            else
            {
                float cm = margin.x + margin.z;
                min = Max(min, metrics.Min + cm);
                preferred = Max(preferred, metrics.Preferred + cm);
                flexible += metrics.Flexible;
            }
        }

        float padding = direction == LayoutDirection.Horizontal ? _padLeft + _padRight : _padTop + _padBottom;
        float spacing = direction == LayoutDirection.Vertical ? _spacing * Max(0, count - 1) : 0f;

        if (direction == LayoutDirection.Vertical)
        {
            _minHeight = min + padding + spacing;
            _preferredHeight = preferred + padding + spacing;
            _flexibleHeight = flexible;
        }
        else
        {
            _minWidth = min + padding;
            _preferredWidth = preferred + padding;
            // Cross-axis flexibility is the SUM of children's (Largest min/preferred + Sum flexible). The
            // old flexible/count divisor silently eroded a nested flexible child's pull to ~0, collapsing
            // it under a ForceExpand=false parent. -xlinka
            _flexibleWidth = flexible;
        }
    }

    private static float Min(float a, float b) => a < b ? a : b;
    private static float Max(float a, float b) => a > b ? a : b;
    private static int Max(int a, int b) => a > b ? a : b;
    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
