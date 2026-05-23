// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// arranges children left-to-right within the layout's rect. equal width per child for now.
// TODO - xlinka: per-child flexible/preferred sizing once metrics solver lands
public class HorizontalLayout : LayoutController
{
    public readonly Sync<float> Spacing;
    public readonly Sync<float> PaddingLeft;
    public readonly Sync<float> PaddingRight;
    public readonly Sync<float> PaddingTop;
    public readonly Sync<float> PaddingBottom;

    private float _spacing;
    private float _padLeft;
    private float _padRight;
    private float _padTop;
    private float _padBottom;

    public HorizontalLayout()
    {
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
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
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float innerWidth = rect.width - _padLeft - _padRight - _spacing * (children.Count - 1);
        if (innerWidth < 0f) innerWidth = 0f;

        float childWidth = innerWidth / children.Count;
        float yMin = rect.yMin + _padBottom;
        float yMax = rect.yMax - _padTop;
        float x = rect.xMin + _padLeft;

        for (int i = 0; i < children.Count; i++)
        {
            children[i].SetLocalComputeRect(new Rect(x, yMin, childWidth, yMax - yMin));
            x += childWidth + _spacing;
        }
    }
}
