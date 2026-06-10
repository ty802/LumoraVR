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

    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private bool _forceExpandWidth;
    private bool _forceExpandHeight;
    private bool _centerChildren;
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
    }

    protected override void FlagChanges(RectTransform rect)
    {
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
        _centerChildren = CenterChildren.Value;
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
            _metrics.Add(LayoutSizing.IsIgnored(children[i])
                ? default
                : LayoutSizing.GetMetrics(children[i], LayoutDirection.Horizontal));
        }

        LayoutSizing.Distribute(innerWidth, _spacing, _padLeft, _metrics, _elements, _forceExpandWidth);

        if (_centerChildren && !_forceExpandWidth && _elements.Count > 0)
        {
            var first = _elements[0];
            var last = _elements[_elements.Count - 1];
            float contentWidth = (last.Offset + last.Size) - first.Offset;
            float shift = (innerWidth - contentWidth) * 0.5f;
            if (shift > 0f)
            {
                for (int i = 0; i < _elements.Count; i++)
                {
                    var element = _elements[i];
                    element.Offset += shift;
                    _elements[i] = element;
                }
            }
        }

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            var element = _elements[i];
            float childHeight = availableHeight;
            float childY = yMin;

            if (!_forceExpandHeight)
            {
                var vertical = LayoutSizing.GetMetrics(children[i], LayoutDirection.Vertical);
                childHeight = Clamp(vertical.Flexible > 0f ? availableHeight : Max(vertical.Min, Min(vertical.Preferred, availableHeight)), 0f, availableHeight);
                childY = yMin + (availableHeight - childHeight) * 0.5f;
            }

            children[i].SetLocalComputeRect(new Rect(rect.xMin + element.Offset, childY, element.Size, childHeight));
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
            var metrics = LayoutSizing.GetMetrics(RectTransform.RectChildren[i], direction);
            if (direction == LayoutDirection.Horizontal)
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
            _flexibleHeight = count > 0 ? flexible / count : 0f;
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
