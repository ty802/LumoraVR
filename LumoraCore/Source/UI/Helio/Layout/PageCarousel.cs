// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// A row of pages under one parent, one shown at a time, sliding from one to the next: the tabs of a picker, the
// steps of a wizard. The current page fills the parent, its neighbours sit just off either side, and a page
// more than one step away is switched off. A change of page eases over SlideTime seconds.
//
// Idle once a slide has landed: nothing is written until the index or a member changes again. -xlinka
[ComponentCategory("UI/Helio/Layout")]
[DefaultUpdateOrder(-50)]
public class PageCarousel : Component
{
    public enum SlideDirection
    {
        LeftToRight,
        RightToLeft,
        TopToBottom,
        BottomToTop,
    }

    public readonly Sync<int> Index;

    public readonly Sync<float> SlideTime;

    public readonly Sync<SlideDirection> Direction;

    public readonly SyncList<CarouselPage> Pages;

    private float _position = float.NaN;
    private bool _dirty = true;

    public PageCarousel()
    {
        Index = new Sync<int>(this, 0);
        SlideTime = new Sync<float>(this, 0.25f);
        Direction = new Sync<SlideDirection>(this, SlideDirection.LeftToRight);
        Pages = new SyncList<CarouselPage>();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _dirty = true;
    }

    public override void OnUpdate(float delta)
    {
        int count = Pages.Count;
        if (count == 0)
            return;
        float target = System.Math.Clamp(Index.Value, 0, count - 1);
        if (float.IsNaN(_position))
            _position = target;
        if (_position == target && !_dirty)
            return;
        _dirty = false;

        float time = SlideTime.Value;
        float step = time > 0f ? delta / time : 1f;
        _position = _position < target ? MathF.Min(_position + step, target) : MathF.Max(_position - step, target);

        for (int i = 0; i < count; i++)
            Place(Pages[i], i - _position);
    }

    private void Place(CarouselPage page, float at)
    {
        bool shown = at > -1f && at < 1f;
        if (page.Active.IsLinkValid)
            page.Active.SetValue(shown);
        if (!shown)
            return;

        // Ease out of and into rest so a slide starts and lands softly.
        float a = MathF.Abs(at);
        float eased = a * a * a * (a * (a * 6f - 15f) + 10f) * MathF.Sign(at);
        var (min, max) = Direction.Value switch
        {
            SlideDirection.RightToLeft => (new float2(-eased, 0f), new float2(1f - eased, 1f)),
            SlideDirection.TopToBottom => (new float2(0f, eased), new float2(1f, 1f + eased)),
            SlideDirection.BottomToTop => (new float2(0f, -eased), new float2(1f, 1f - eased)),
            _ => (new float2(eased, 0f), new float2(1f + eased, 1f)),
        };
        if (page.AnchorMin.IsLinkValid)
            page.AnchorMin.SetValue(min);
        if (page.AnchorMax.IsLinkValid)
            page.AnchorMax.SetValue(max);
    }
}
