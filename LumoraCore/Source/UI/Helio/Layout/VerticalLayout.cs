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

    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private bool _forceExpandWidth;
    private bool _forceExpandHeight;
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
    }

    protected override void FlagChanges(RectTransform rect)
    {
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
            _metrics.Add(LayoutSizing.GetMetrics(children[i], LayoutDirection.Vertical));
        }

        LayoutSizing.Distribute(innerHeight, _spacing, _padTop, _metrics, _elements, _forceExpandHeight);

        for (int i = 0; i < children.Count; i++)
        {
            var element = _elements[i];
            float childWidth = availableWidth;
            float childX = xMin;

            if (!_forceExpandWidth)
            {
                var horizontal = LayoutSizing.GetMetrics(children[i], LayoutDirection.Horizontal);
                childWidth = Clamp(horizontal.Flexible > 0f ? availableWidth : Max(horizontal.Min, Min(horizontal.Preferred, availableWidth)), 0f, availableWidth);
                childX = xMin + (availableWidth - childWidth) * 0.5f;
            }

            float yBottom = rect.yMax - element.Offset - element.Size;
            children[i].SetLocalComputeRect(new Rect(childX, yBottom, childWidth, element.Size));
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
            var metrics = LayoutSizing.GetMetrics(RectTransform.RectChildren[i], direction);
            if (direction == LayoutDirection.Vertical)
            {
                min += metrics.Min;
                preferred += metrics.Preferred;
                flexible += metrics.Flexible;
            }
            else
            {
                min = Max(min, metrics.Min);
                preferred = Max(preferred, metrics.Preferred);
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
            _flexibleWidth = count > 0 ? flexible / count : 0f;
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
