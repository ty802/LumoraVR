// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// arranges children top-to-bottom within the layout's rect. equal height per child for now.
// TODO - xlinka: per-child flexible/preferred sizing once metrics solver lands
public class VerticalLayout : LayoutController
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

    public VerticalLayout()
    {
        Spacing = new Sync<float>(this, 0f);
        PaddingLeft = new Sync<float>(this, 0f);
        PaddingRight = new Sync<float>(this, 0f);
        PaddingTop = new Sync<float>(this, 0f);
        PaddingBottom = new Sync<float>(this, 0f);
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
    }

    public override void ArrangeChildren(IReadOnlyList<RectTransform> children)
    {
        if (RectTransform == null || children.Count == 0) return;

        var rect = RectTransform.LocalComputeRect;
        float innerHeight = rect.height - _padTop - _padBottom - _spacing * (children.Count - 1);
        if (innerHeight < 0f) innerHeight = 0f;

        float childHeight = innerHeight / children.Count;
        float xMin = rect.xMin + _padLeft;
        float xMax = rect.xMax - _padRight;
        // top-to-bottom in canvas coords where +y is up. start from yMax. - xlinka
        float yTop = rect.yMax - _padTop;

        for (int i = 0; i < children.Count; i++)
        {
            float yBottom = yTop - childHeight;
            children[i].SetLocalComputeRect(new Rect(xMin, yBottom, xMax - xMin, childHeight));
            yTop = yBottom - _spacing;
        }
    }
}
