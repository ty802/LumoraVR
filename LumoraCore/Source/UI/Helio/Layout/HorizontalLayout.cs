// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// arranges children left-to-right within the layout's rect.
public class HorizontalLayout : LayoutController
{
    public readonly Sync<float> Spacing;
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;
    public readonly Sync<bool> ForceExpandWidth;
    public readonly Sync<bool> ForceExpandHeight;
    public readonly Sync<bool> CenterChildren;
    // Where children sit on the cross (vertical) axis when not force-expanded. Default Center = prior behavior.
    public readonly Sync<LayoutAlignment> CrossAlignment;
    // How children distribute along the main (horizontal) axis when not force-expanded (justify-content).
    public readonly Sync<MainAxisAlignment> MainAlignment;

    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private bool _forceExpandWidth;
    private bool _forceExpandHeight;
    private bool _centerChildren;
    private LayoutAlignment _crossAlignment;
    private MainAxisAlignment _mainAlignment;
    private readonly List<LayoutMetrics> _metrics = new();
    private readonly List<LayoutSizing.Element> _elements = new();

    public HorizontalLayout()
    {
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
        ForceExpandWidth = new Sync<bool>(this, true);
        ForceExpandHeight = new Sync<bool>(this, true);
        CenterChildren = new Sync<bool>(this, false);
        CrossAlignment = new Sync<LayoutAlignment>(this, LayoutAlignment.Center);
        MainAlignment = new Sync<MainAxisAlignment>(this, MainAxisAlignment.Start);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        // A HorizontalLayout also sizes children on the cross (vertical) axis (PaddingTop/Bottom,
        // ForceExpandHeight, CrossAlignment), so a change invalidates BOTH axes' metrics. -xlinka
        rect.MarkInvalidateHorizontalLayout();
        rect.MarkInvalidateVerticalLayout();
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
        _centerChildren = CenterChildren.Value;
        _crossAlignment = CrossAlignment.Value;
        _mainAlignment = MainAlignment.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float innerWidth = rect.width - _padLeft - _padRight;
        if (innerWidth < 0f) innerWidth = 0f;

        float yMin = rect.yMin + _padBottom;
        float yMax = rect.yMax - _padTop;
        float availableHeight = yMax - yMin;
        if (availableHeight < 0f) availableHeight = 0f;

        _metrics.Clear();
        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i]))
            {
                _metrics.Add(default);
                continue;
            }
            var m = LayoutSizing.Measured(children[i], LayoutDirection.Horizontal);
            var margin = LayoutSizing.GetMargin(children[i]);
            float mainMargin = margin.x + margin.z; // left + right
            m.Min += mainMargin;
            m.Preferred += mainMargin;
            _metrics.Add(m);
        }

        LayoutSizing.Distribute(innerWidth, _spacing, _padLeft, _metrics, _elements, _forceExpandWidth);

        if (!_forceExpandWidth)
        {
            // CenterChildren stays as a back-compat shortcut for the common case.
            var mainAlign = _mainAlignment;
            if (_centerChildren && mainAlign == MainAxisAlignment.Start)
                mainAlign = MainAxisAlignment.Center;
            LayoutSizing.AlignMainAxis(_elements, innerWidth, _padLeft, _spacing, mainAlign);
        }

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            var element = _elements[i];
            var margin = LayoutSizing.GetMargin(children[i]);
            float mainMargin = margin.x + margin.z;  // left + right
            float crossMargin = margin.y + margin.w; // bottom + top

            float childX = rect.xMin + element.Offset + margin.x;
            float childWidth = element.Size - mainMargin;
            if (childWidth < 0f) childWidth = 0f;

            float cellHeight = availableHeight - crossMargin;
            if (cellHeight < 0f) cellHeight = 0f;
            float childHeight = cellHeight;
            float childY = yMin + margin.y;

            if (!_forceExpandHeight)
            {
                var vertical = LayoutSizing.Measured(children[i], LayoutDirection.Vertical);
                childHeight = Clamp(vertical.Flexible > 0f ? cellHeight : Max(vertical.Min, Min(vertical.Preferred, cellHeight)), 0f, cellHeight);
                // Cross-axis (vertical, y-up): Start=top, End=bottom, Center=middle.
                float align = _crossAlignment switch { LayoutAlignment.Start => 1f, LayoutAlignment.End => 0f, _ => 0.5f };
                childY = yMin + margin.y + (cellHeight - childHeight) * align;
            }

            children[i].SetLocalComputeRect(new Rect(childX, childY, childWidth, childHeight));
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
            if (direction == LayoutDirection.Horizontal)
            {
                float mm = margin.x + margin.z;
                min += metrics.Min + mm;
                preferred += metrics.Preferred + mm;
                flexible += metrics.Flexible;
            }
            else
            {
                float cm = margin.y + margin.w;
                min = Max(min, metrics.Min + cm);
                preferred = Max(preferred, metrics.Preferred + cm);
                flexible += metrics.Flexible;
            }
        }

        float padding = direction == LayoutDirection.Horizontal ? _padLeft + _padRight : _padTop + _padBottom;
        float spacing = direction == LayoutDirection.Horizontal ? _spacing * Max(0, count - 1) : 0f;

        if (direction == LayoutDirection.Horizontal)
        {
            _minWidth = min + padding + spacing;
            _preferredWidth = preferred + padding + spacing;
            _flexibleWidth = flexible;
        }
        else
        {
            _minHeight = min + padding;
            _preferredHeight = preferred + padding;
            // Cross-axis flexibility is the SUM of children's, not flexible/count (the divisor eroded a
            // nested flexible child's pull to ~0 and collapsed it under a ForceExpand=false parent). -xlinka
            _flexibleHeight = flexible;
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
