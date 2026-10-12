// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// stacks every child in the same place, each filling the layout's inner rect. metrics are the
// MAX of the children on both axes (not the sum), so the container shrink-wraps to its largest
// child. useful for layered content (background + foreground, overlapping panels). An axis that does
// not force-expand gives each child its own size there (all of it when flexible, else its preferred size
// up to the room, never under its minimum) and places it by that axis's alignment. -xlinka
[ComponentCategory("UI/Helio/Layout")]
public class OverlappingLayout : LayoutController
{
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;
    public readonly Sync<bool> ForceExpandWidth;
    public readonly Sync<bool> ForceExpandHeight;
    // Start is left / top.
    public readonly Sync<LayoutAlignment> HorizontalAlignment;
    public readonly Sync<LayoutAlignment> VerticalAlignment;

    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;
    private bool _expandWidth;
    private bool _expandHeight;
    private LayoutAlignment _alignX;
    private LayoutAlignment _alignY;

    public OverlappingLayout()
    {
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
        ForceExpandWidth = new Sync<bool>(this, true);
        ForceExpandHeight = new Sync<bool>(this, true);
        HorizontalAlignment = new Sync<LayoutAlignment>(this, LayoutAlignment.Center);
        VerticalAlignment = new Sync<LayoutAlignment>(this, LayoutAlignment.Center);
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
        _expandWidth = ForceExpandWidth.Value;
        _expandHeight = ForceExpandHeight.Value;
        _alignX = HorizontalAlignment.Value;
        _alignY = VerticalAlignment.Value;
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        // Arranged in the rect as placed: a RectLerp on this slot moves the rect and what is anchored to it,
        // not what this layout arranges.
        var rect = RectTransform.PlacedComputeRect;
        float x = rect.xMin + _padLeft;
        float y = rect.yMin + _padBottom;
        float w = rect.width - _padLeft - _padRight;
        float h = rect.height - _padTop - _padBottom;
        if (w < 0f) w = 0f;
        if (h < 0f) h = 0f;

        for (int i = 0; i < children.Count; i++)
        {
            if (LayoutSizing.IsIgnored(children[i])) continue;
            float cx = x, cy = y, cw = w, ch = h;
            if (!_expandWidth)
            {
                cw = OwnSize(LayoutSizing.Measured(children[i], LayoutDirection.Horizontal), w);
                cx = x + (w - cw) * (_alignX switch { LayoutAlignment.Start => 0f, LayoutAlignment.End => 1f, _ => 0.5f });
            }
            if (!_expandHeight)
            {
                ch = OwnSize(LayoutSizing.Measured(children[i], LayoutDirection.Vertical), h);
                cy = y + (h - ch) * (_alignY switch { LayoutAlignment.Start => 1f, LayoutAlignment.End => 0f, _ => 0.5f });
            }
            children[i].SetLocalComputeRect(new Rect(cx, cy, cw, ch));
        }
    }

    private static float OwnSize(in LayoutMetrics metrics, float room)
        => metrics.Flexible > 0f ? room : Max(metrics.Min, Min(metrics.Preferred, room));

    private readonly List<RectTransform> _measureChildren = new();

    public override void EnsureValidMetrics(LayoutDirection direction)
    {
        if (RectTransform == null) return;

        float min = 0f;
        float preferred = 0f;
        float flexible = 0f;
        float flexibleSum = 0f;
        LayoutSizing.CollectMeasureChildren(RectTransform, _measureChildren);
        int count = 0;

        for (int i = 0; i < _measureChildren.Count; i++)
        {
            if (LayoutSizing.IsIgnored(_measureChildren[i])) continue;
            count++;
            var metrics = LayoutSizing.Measured(_measureChildren[i], direction);
            min = Max(min, metrics.Min);
            preferred = Max(preferred, metrics.Preferred);
            flexible = Max(flexible, metrics.Flexible);
            flexibleSum += metrics.Flexible;
        }
        // Per-metric rules average the children's pull instead.
        if (RectTransform.Canvas is { PerMetricPriority.Value: true })
            flexible = count > 0 ? flexibleSum / count : 0f;

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
    private static float Min(float a, float b) => a < b ? a : b;
}
