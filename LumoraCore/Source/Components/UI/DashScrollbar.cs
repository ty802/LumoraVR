// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The dash's draggable scrollbar: a track down the right edge and a handle sized to the fraction of the
// content you can see.
//
// One class instead of one per screen. The Session settings form grew this first and the permissions
// page needed the same thing, and a copy would have drifted: the "wait for a real viewport rect" retry,
// the clamp that stops a shrunk content resting past its own bottom and the hide-when-it-fits rule are
// all things you get wrong once and then only on one of the two pages. The two placements differ and
// nothing else does, so that is the only thing the two entry points disagree about. -xlinka
internal sealed class DashScrollbar
{
    public const float Width = 18f;

    // A freshly-built viewport sits at the RectTransform default (100x100) until the canvas runs a layout
    // pass. Sizing off that 100 makes maxScroll = content - 100 (huge) with a tiny thumb, so a short form
    // scrolls out of view into empty space. This sentinel rejects the un-laid-out rect; a real dash
    // viewport is several hundred units, so a legitimate size is never refused. -xlinka
    private const float LaidOutViewportFloor = 100f;
    private const float MinHandleHeight = 30f;
    private const float HandleInset = 2f;
    private const int MaxRetries = 30;

    private readonly Slot _track;
    private readonly RectTransform _handle;

    private ScrollRect? _scroll;
    private RectTransform? _viewport;
    private RectTransform? _content;
    private Action<ScrollRect, float2>? _scrollChanged;
    private Action? _moved;
    private float _pressY;
    private float _pressScroll;
    private int _retries;
    private bool _retryQueued;

    private DashScrollbar(Slot track, RectTransform handle)
    {
        _track = track;
        _handle = handle;
    }

    // Sized by a LayoutElement, for a screen whose scroll area is a HorizontalLayout: hiding the track
    // then gives its width back to the viewport beside it.
    public static DashScrollbar InColumn(Slot area)
    {
        var track = area.AddSlot("Scrollbar");
        track.AttachComponent<RectTransform>();
        var element = track.AttachComponent<LayoutElement>();
        element.MinWidth.Value = Width;
        element.PreferredWidth.Value = Width;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        return Build(track);
    }

    // Anchored to the parent's right edge, for a page that places its viewport by hand. The caller keeps
    // the viewport clear of the track; the inset does not move when the track hides, because a row set
    // that reflows sideways every time the content crosses the fit threshold reads as a glitch.
    public static DashScrollbar OnRightEdge(Slot host, float right, float top, float bottom)
    {
        var track = SettingsUI.Child(host, "Scrollbar", new float2(1f, 0f), new float2(1f, 1f),
            new float2(-(right + Width), bottom), new float2(-right, -top));
        return Build(track);
    }

    private static DashScrollbar Build(Slot track)
    {
        SettingsUI.Panel(track, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var handleSlot = track.AddSlot("Handle");
        var handle = handleSlot.AttachComponent<RectTransform>();
        handle.AnchorMin.Value = new float2(0f, 1f);
        handle.AnchorMax.Value = new float2(1f, 1f);
        handle.OffsetMin.Value = new float2(HandleInset, -(MinHandleHeight * 2f));
        handle.OffsetMax.Value = new float2(-HandleInset, 0f);
        SettingsUI.Panel(handleSlot, DashTheme.Accent, color.Transparent, DashTheme.RadiusControl);

        var bar = new DashScrollbar(track, handle);

        // The handle is the draggable element (classic scrollbar feel); the viewport's own ScrollRect
        // already takes drag-on-content and the wheel.
        var interaction = handleSlot.AttachComponent<InteractionElement>();
        interaction.Pressed += bar.OnPress;
        interaction.Dragged += bar.OnDrag;
        return bar;
    }

    // Point at the scroll this bar drives. moved runs after the bar itself changes the scroll, for the
    // screen to dirty its canvas; the ScrollRect's own event covers every other way it can move.
    public void Bind(ScrollRect? scroll, RectTransform? viewport, RectTransform? content, Action? moved = null)
    {
        if (_scroll != null && _scrollChanged != null && !_scroll.IsDestroyed)
            _scroll.ScrollChanged -= _scrollChanged;

        _scroll = scroll;
        _viewport = viewport;
        _content = content;
        _moved = moved;
        _retries = 0;
        _retryQueued = false;

        if (_scroll == null)
            return;
        _scrollChanged = (_, _) => Refresh();
        _scroll.ScrollChanged += _scrollChanged;
    }

    private void OnPress(UIInteractionContext context)
    {
        _pressY = context.LocalPoint.y;
        _pressScroll = _scroll?.AbsolutePosition.y ?? 0f;
    }

    private void OnDrag(UIInteractionContext context)
    {
        if (!Measure(out float viewportHeight, out float contentHeight))
            return;
        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0f)
            return;
        float travel = viewportHeight - HandleHeight(viewportHeight, contentHeight);
        if (travel <= 0f)
            return;

        // Dragging the handle down (local Y decreases) scrolls the content down.
        float deltaY = context.LocalPoint.y - _pressY;
        float scrolled = _pressScroll - deltaY * (maxScroll / travel);
        _scroll!.AbsolutePosition = new float2(0f, System.Math.Clamp(scrolled, 0f, maxScroll));
        Refresh();
        _moved?.Invoke();
    }

    // Size and place the handle for whatever the content is now. Safe to call every frame and cheap when
    // nothing moved: every write underneath is equality-gated.
    public void Refresh()
    {
        if (_scroll == null || _track == null || _track.IsDestroyed || _handle == null || _handle.IsDestroyed)
            return;
        if (!Measure(out float viewportHeight, out float contentHeight))
        {
            // Layout has not produced the real rects yet. Retry next frame, bounded, so a genuinely tiny
            // or broken panel cannot churn forever. One retry in flight at a time: a screen that calls
            // this from its own tick would otherwise queue a fresh one every frame it waits. -xlinka
            if (!_retryQueued && _retries++ < MaxRetries)
            {
                _retryQueued = true;
                _track.World?.RunInUpdates(1, RetryRefresh);
            }
            return;
        }
        _retries = 0;

        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0.5f)
        {
            if (_track.ActiveSelf.Value)
                _track.ActiveSelf.Value = false;
            // Drop any stale scroll so the page sits at the top rather than at an offset it can no longer
            // reach.
            if (_scroll.NormalizedPosition.y != 0f)
                _scroll.NormalizedPosition = float2.Zero;
            return;
        }
        if (!_track.ActiveSelf.Value)
            _track.ActiveSelf.Value = true;

        // Clamp an existing scroll to the (possibly reduced) real range so it cannot rest past the bottom.
        if (_scroll.AbsolutePosition.y > maxScroll)
            _scroll.AbsolutePosition = new float2(0f, maxScroll);

        float handleHeight = HandleHeight(viewportHeight, contentHeight);
        float fraction = System.Math.Clamp(_scroll.AbsolutePosition.y / maxScroll, 0f, 1f);
        float offset = fraction * (viewportHeight - handleHeight);
        SettingsUI.SetOffsets(_handle, new float2(HandleInset, -(offset + handleHeight)),
            new float2(-HandleInset, -offset));
    }

    private void RetryRefresh()
    {
        _retryQueued = false;
        Refresh();
    }

    private bool Measure(out float viewportHeight, out float contentHeight)
    {
        viewportHeight = _viewport?.LocalComputeRect.height ?? 0f;
        contentHeight = _content?.LocalComputeRect.height ?? 0f;
        return _scroll != null && !_scroll.IsDestroyed
            && viewportHeight > LaidOutViewportFloor && contentHeight > 0f;
    }

    private static float HandleHeight(float viewportHeight, float contentHeight)
        => MathF.Max(MinHandleHeight, viewportHeight * (viewportHeight / contentHeight));
}
