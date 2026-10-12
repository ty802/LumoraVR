// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

// A button that answers inside its rect with the corners rounded off. A corner of half the short side makes
// the area a circle, so round controls packed on a ring keep their corners out of each other's way. -xlinka
[ComponentCategory("UI/Helio/Interaction")]
public sealed class RoundedButton : Button
{
    public readonly Sync<float> CornerRadius;

    public RoundedButton()
    {
        CornerRadius = new Sync<float>(this, 0f);
    }

    public override bool IsPointInside(in float2 point)
    {
        var rectTransform = RectTransform;
        if (rectTransform == null)
            return false;

        var rect = rectTransform.LocalComputeRect;
        // A grown hit area answers across its whole rect. The rounding is for controls packed corner to
        // corner, and an outset already spills past the drawn corners into the gap.
        if (HitOutset.Value > 0f)
            return HitRect(rect).Contains(point);
        if (!rect.Contains(point))
            return false;

        float r = MathF.Min(MathF.Max(CornerRadius.Value, 0f), MathF.Min(rect.width, rect.height) * 0.5f);
        if (r <= 0f)
            return true;

        float cx = MathF.Min(MathF.Max(point.x, rect.xMin + r), rect.xMax - r);
        float cy = MathF.Min(MathF.Max(point.y, rect.yMin + r), rect.yMax - r);
        float dx = point.x - cx;
        float dy = point.y - cy;
        return dx * dx + dy * dy <= r * r;
    }
}
