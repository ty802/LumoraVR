// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

[ComponentCategory("UI/Helio/Interaction")]
public sealed class ScrollRect : InteractionElement, IUIAxisActionReceiver
{
    public readonly SyncRef<RectTransform> Content;
    // Persisted scroll position is NORMALIZED 0..1 per axis - it holds its proportional place when the content
    // or viewport resizes, and a normalized value can never produce an out-of-range scroll against a wrong
    // content size. The Sync field name stays 'Scroll' for wire compatibility; work in pixels via
    // AbsolutePosition. -xlinka
    public readonly Sync<float2> Scroll;
    public readonly Sync<float2> ScrollSensitivity;

    // A sideways-only strip (the session strip in the world browser) has no vertical travel, so a wheel
    // over it would do nothing at all. Set this and the wheel's y notches drive x instead. Drag and
    // programmatic scrolling are untouched. -xlinka
    public readonly Sync<bool> WheelScrollsHorizontal;

    // A RectTransform sits at its 100x100 default until the canvas runs a layout pass. Applying scroll against
    // that default (excess = content - 100) could shove the content out of the also-stale clip rect. Reject a
    // viewport at/under this floor and wait for a real laid-out rect (the next cycle re-runs ApplyScroll). -xlinka
    private const float UnlaidViewportFloor = 100f;

    private float2 _pressPoint;
    private float2 _pressAbsolute;

    // Last known scrollable excess (content - viewport, per axis). Only a fallback for the frames where the
    // rects cannot be read yet; ScrollableExcess() is what everything actually uses. -xlinka
    private float2 _excess;

    // Content height handed over by a virtualised list (see SetContentExtent). Negative means nobody told us
    // and the measured content rect is the only source. -xlinka
    private float _contentExtent = -1f;

    // The content's own graphic chunk. Render-offset scrolling moves THIS chunk (its mesh is baked once)
    // instead of re-tessellating, so big lists scroll cheaply. -xlinka
    private GraphicChunkRoot? _contentRoot;
    private bool _scrollSetupDone;

    public event Action<ScrollRect, float2>? ScrollChanged;

    public ScrollRect()
    {
        Content = new SyncRef<RectTransform>(this);
        Scroll = new Sync<float2>(this, float2.Zero);
        ScrollSensitivity = new Sync<float2>(this, float2.One);
        WheelScrollsHorizontal = new Sync<bool>(this, false);
    }

    // normalized 0..1 scroll position (the persisted value), clamped on set
    public float2 NormalizedPosition
    {
        get => Scroll.Value;
        set => SetNormalized(new float2(Clamp01(value.x), Clamp01(value.y)));
    }

    // pixels = normalized * scrollable excess. setting converts back to normalized against the
    // live excess (a zero-excess axis keeps its value, so no divide-by-zero).
    public float2 AbsolutePosition
    {
        get
        {
            var excess = ScrollableExcess();
            return new float2(Scroll.Value.x * excess.x, Scroll.Value.y * excess.y);
        }
        set
        {
            var excess = ScrollableExcess();
            SetNormalized(new float2(
                excess.x > 0f ? Clamp01(value.x / excess.x) : Scroll.Value.x,
                excess.y > 0f ? Clamp01(value.y / excess.y) : Scroll.Value.y));
        }
    }

    // A list that pins its own content height tells us the number here.
    //
    // It knows the page height a full frame before any layout pass can measure the rect, and it is the only
    // writer of that rect's size, so when it has spoken it beats the measured height. Without this the range
    // and the drawn offset both wait on a pass that a scoped relayout never runs, and a page whose height
    // changed under a parked scroll ends up drawn at one offset while its rows were realized for another -
    // which reads as a band of the viewport going blank. -xlinka
    public void SetContentExtent(float height)
    {
        if (height < 0f)
            height = 0f;
        if (_contentExtent == height)
            return;
        _contentExtent = height;

        // The same normalized position over a different range is a different PIXEL position, so the content
        // has to be re-drawn where the freshly realized rows expect to find it.
        var before = _excess;
        if (ScrollableExcess() != before)
            ApplyScrollNow();
    }

    // Scrollable excess read from the LIVE rects, not from whenever the canvas last ran a full layout pass.
    //
    // This number used to be written only inside ApplyScroll, which the canvas calls from the FULL layout
    // branch alone. A virtualised list sets its page height by writing the content rect, and that write
    // reaches the canvas on the SCOPED relayout path (the scrolled content is its own graphic chunk), which
    // re-anchors the rect but never re-runs the scroll pass. So a page taller than the previous one kept the
    // previous page's range - zero when you came from a page that fit - the wheel clamped against it, and
    // nothing could break the deadlock, because a scroll that cannot move dirties no layout. -xlinka
    private float2 ScrollableExcess()
    {
        var viewport = RectTransform;
        var content = Content.Target;
        if (viewport == null || content == null)
            return _excess;

        var viewportRect = viewport.LocalComputeRect;
        var contentRect = content.LocalComputeRect;
        if (viewportRect.IsEmpty || contentRect.IsEmpty)
            return _excess;
        // Same un-laid-out guard ApplyScroll uses - see the field doc on UnlaidViewportFloor.
        if (viewportRect.width <= UnlaidViewportFloor || viewportRect.height <= UnlaidViewportFloor)
            return _excess;

        float contentHeight = _contentExtent >= 0f ? _contentExtent : contentRect.height;
        _excess = new float2(
            MathF.Max(0f, contentRect.width - viewportRect.width),
            MathF.Max(0f, contentHeight - viewportRect.height));
        return _excess;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        ApplyScrollNow();
    }

    // Apply the current scroll position by sliding the content chunk's clip_offset (no rebuild, no
    // re-tessellation). Only forces a layout pass when the content chunk isn't built yet (the very first scroll),
    // so the chunk gets baked and ApplyScroll can position it. -xlinka
    private void ApplyScrollNow()
    {
        var canvas = FindCanvas();
        if (canvas == null)
            return;
        EnsureScrollSetup();
        // Content slides left/up as you scroll right/down (same sign as the rebuild path in ApplyScroll).
        var absolute = AbsolutePosition;
        if (_contentRoot != null && canvas.ApplyScrollOffset(_contentRoot, new float2(-absolute.x, absolute.y)))
            return;
        canvas.MarkLayoutDirty();
    }

    // One-time: give the content its own graphic chunk so render-offset scrolling can move it as a unit.
    private void EnsureScrollSetup()
    {
        if (_scrollSetupDone)
            return;
        var contentSlot = Content.Target?.Slot;
        if (contentSlot == null)
            return;
        _contentRoot = contentSlot.GetComponent<GraphicChunkRoot>() ?? contentSlot.AttachComponent<GraphicChunkRoot>();
        _contentRoot.ScrollContent = true;
        _scrollSetupDone = true;
    }

    private Canvas? FindCanvas()
    {
        for (var slot = Slot; slot != null; slot = slot.Parent)
        {
            var canvas = slot.GetComponent<Canvas>();
            if (canvas != null)
                return canvas;
        }
        return null;
    }

    protected override void OnPress(in UIInteractionContext context)
    {
        // Viewport space, not raw canvas space: for a scroll nested in another scroll the outer offset can
        // move mid-gesture (wheel, second pointer), and a raw press anchor would make this drag jump by the
        // outer delta. Same space on both ends, so the delta is pure pointer motion. -xlinka
        _pressPoint = context.PointIn(Slot);
        _pressAbsolute = AbsolutePosition;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        var delta = context.PointIn(Slot) - _pressPoint;
        var sensitivity = ScrollSensitivity.Value;
        AbsolutePosition = new float2(
            _pressAbsolute.x - delta.x * sensitivity.x,
            _pressAbsolute.y - delta.y * sensitivity.y);
    }

    public bool ProcessAxis(in UIInteractionContext context, in float2 axis)
    {
        if (axis == float2.Zero)
        {
            return false;
        }

        var sensitivity = ScrollSensitivity.Value;
        var abs = AbsolutePosition;
        var before = Scroll.Value;
        if (WheelScrollsHorizontal.Value)
        {
            // Same 24-unit notch step as the vertical path, and the same sign, so a wheel-down that would
            // scroll a list further in scrolls a strip further right.
            AbsolutePosition = new float2(
                abs.x - (axis.x * sensitivity.x + axis.y * sensitivity.y * 24f),
                abs.y);
        }
        else
        {
            AbsolutePosition = new float2(
                abs.x - axis.x * sensitivity.x,
                abs.y - axis.y * sensitivity.y * 24f);
        }
        return Scroll.Value != before;
    }

    internal bool ApplyScroll(out RectTransform? content)
    {
        content = Content.Target;
        var viewport = RectTransform;
        if (viewport == null || content == null)
        {
            return false;
        }

        var viewportRect = viewport.LocalComputeRect;
        var contentRect = content.LocalComputeRect;
        if (viewportRect.IsEmpty || contentRect.IsEmpty)
        {
            return false;
        }

        // Don't apply scroll against an un-laid-out viewport (still the 100x100 default) - see field doc.
        if (viewportRect.width <= UnlaidViewportFloor || viewportRect.height <= UnlaidViewportFloor)
        {
            return false;
        }

        var excess = ScrollableExcess();

        var norm = new float2(Clamp01(Scroll.Value.x), Clamp01(Scroll.Value.y));
        if (norm != Scroll.Value)
        {
            Scroll.Value = norm;
        }

        var absolute = new float2(norm.x * excess.x, norm.y * excess.y);

        // Position lives in the content's mesh-chunk offset, NEVER in its rect. We only store the current offset
        // here (a structural rebuild just baked the chunk); live scrolling moves it via ApplyScrollOffset. The
        // content rect is owned solely by its ContentSizeFitter (size), so the two never fight. Always return false
        // - the caller never re-lays-out the content for a scroll. -xlinka
        EnsureScrollSetup();
        if (_contentRoot != null)
            _contentRoot.RenderOffset = new float2(-absolute.x, absolute.y);
        return false;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    private void SetNormalized(float2 normalized)
    {
        if (Scroll.Value == normalized) return;

        Scroll.Value = normalized;
        ScrollChanged?.Invoke(this, AbsolutePosition);
        // Move the content chunk (render-offset) or dirty the canvas (rect-mutation fallback).
        ApplyScrollNow();
    }
}
