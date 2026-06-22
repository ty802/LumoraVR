// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class Widget : UIComponent
{
    public readonly Sync<float2> MinSize;
    public readonly Sync<float2> PreferredSize;
    public readonly Sync<float2> MaxSize;
    public readonly Sync<float> AspectRatio;
    public readonly Sync<bool> Standalone;
    public readonly Sync<int> GridX;
    public readonly Sync<int> GridY;
    public readonly Sync<int> GridWidth;
    public readonly Sync<int> GridHeight;

    /// <summary>
    /// Smallest cell footprint this widget will accept when the grid is too tight for its full
    /// GridWidth x GridHeight. The grid tries the full size first and shrinks toward this minimum to
    /// fit, instead of failing to place. Defaults to 1x1. -xlinka
    /// </summary>
    public readonly Sync<int> MinGridWidth;
    public readonly Sync<int> MinGridHeight;

    /// <summary>
    /// Authored "intended" cell footprint, captured the first time the widget is placed so that
    /// shrink-to-fit never permanently destroys it - the widget can grow back to this size when room
    /// opens up again. 0 means "not captured yet; treat GridWidth/GridHeight as authored". -xlinka
    /// </summary>
    public readonly Sync<int> PreferredGridWidth;
    public readonly Sync<int> PreferredGridHeight;

    /// <summary>
    /// Prioritized list of acceptable width:height aspect ratios. When non-empty, FitSize picks the
    /// ratio that yields the largest box inside the offered area; empty falls back to the single
    /// AspectRatio (or no constraint). -xlinka
    /// </summary>
    public readonly SyncFieldList<float2> AllowedAspectRatios;

    public Widget()
    {
        MinSize = new Sync<float2>(this, new float2(120f, 80f));
        PreferredSize = new Sync<float2>(this, new float2(240f, 160f));
        MaxSize = new Sync<float2>(this, new float2(900f, 700f));
        AspectRatio = new Sync<float>(this, 0f);
        Standalone = new Sync<bool>(this, false);
        GridX = new Sync<int>(this, 0);
        GridY = new Sync<int>(this, 0);
        GridWidth = new Sync<int>(this, 2);
        GridHeight = new Sync<int>(this, 2);
        MinGridWidth = new Sync<int>(this, 1);
        MinGridHeight = new Sync<int>(this, 1);
        PreferredGridWidth = new Sync<int>(this, 0);
        PreferredGridHeight = new Sync<int>(this, 0);
        AllowedAspectRatios = new SyncFieldList<float2>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRect();
    }

    /// <summary>
    /// Negotiate an offered size into this widget's allowed bounds: reject if below Min, clamp to Max, and
    /// if AspectRatio (w/h) is set, fit the largest box of that ratio inside the offered area. Returns null
    /// when the offer can't satisfy the minimum. -xlinka
    /// </summary>
    public float2? FitSize(float2 offered)
    {
        var min = MinSize.Value;
        var max = MaxSize.Value;

        if (offered.x < min.x || offered.y < min.y)
            return null;

        offered = new float2(MinF(offered.x, max.x), MinF(offered.y, max.y));

        // Prefer the allowed aspect ratio that yields the largest box inside the offered area.
        float2? best = null;
        float bestArea = -1f;
        foreach (var ratio in AllowedAspectRatios)
        {
            if (ratio.x <= 0f || ratio.y <= 0f)
                continue;
            var fit = FitAspect(offered, ratio.x / ratio.y);
            float area = fit.x * fit.y;
            if (area > bestArea)
            {
                bestArea = area;
                best = fit;
            }
        }
        if (best.HasValue)
            return best.Value;

        // No ratio list: fall back to the single AspectRatio, or no constraint.
        float ar = AspectRatio.Value;
        if (ar > 0f)
            return FitAspect(offered, ar);

        return offered;
    }

    // Largest box of aspect `ar` (width/height) that fits inside the offered area.
    private static float2 FitAspect(float2 offered, float ar)
    {
        if (ar <= 0f || offered.x <= 0f || offered.y <= 0f)
            return offered;
        float offeredAr = offered.x / offered.y;
        return offeredAr < ar
            ? new float2(offered.x, offered.x / ar)
            : new float2(offered.y * ar, offered.y);
    }

    private static float MinF(float a, float b) => a < b ? a : b;

    public UIBuilder CreateBuilder()
    {
        EnsureRect();
        return new UIBuilder(Slot);
    }

    public Canvas EnsureCanvas()
    {
        return Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();
    }

    private void EnsureRect()
    {
        _ = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
    }
}
