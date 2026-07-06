// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

/// <summary>
/// Stacks every child in the same place, each filling the layout's inner rect.
/// Metrics are the MAX of the children on both axes (not the sum), so the
/// container shrink-wraps to its largest child. Useful for layered content
/// (background + foreground, overlapping panels) - the CSS "stack" / z-stack.
/// </summary>
public class OverlappingLayout : LayoutController
{
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;

    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;

    public OverlappingLayout()
    {
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
    }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkInvalidateHorizontalLayout();
        rect.MarkInvalidateVerticalLayout();
    }

    public override void PrepareCompute()
    {
        _padLeft = PaddingLeft.Value;
        _padRight = PaddingRight.Value;
        _padTop = PaddingTop.Value;
        _padBottom = PaddingBottom.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float x = rect.xMin + _padLeft;
        float y = rect.yMin + _padBottom;
        float w = rect.width - _padLeft - _padRight;
        float h = rect.height - _padTop - _padBottom;
        if (w < 0f) w = 0f;
        if (h < 0f) h = 0f;

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            children[i].SetLocalComputeRect(new Rect(x, y, w, h));
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
            min = Max(min, metrics.Min);
            preferred = Max(preferred, metrics.Preferred);
            flexible = Max(flexible, metrics.Flexible);
        }

        float padding = direction == LayoutDirection.Horizontal ? _padLeft + _padRight : _padTop + _padBottom;
        if (direction == LayoutDirection.Horizontal)
        {
            _minWidth = min + padding;
            _preferredWidth = preferred + padding;
            _flexibleWidth = flexible;
        }
        else
        {
            _minHeight = min + padding;
            _preferredHeight = preferred + padding;
            _flexibleHeight = flexible;
        }
    }

    private static float Max(float a, float b) => a > b ? a : b;
}
