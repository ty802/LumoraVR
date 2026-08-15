// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class ScrollRect : InteractionElement, IUIAxisActionReceiver
{
    public readonly SyncRef<RectTransform> Content;
    // Persisted scroll position is NORMALIZED 0..1 per axis - it holds its proportional place when the content
    // or viewport resizes, and a normalized value can never produce an out-of-range scroll against a wrong
    // content size. The Sync field name stays 'Scroll' for wire compatibility; work in pixels via
    // AbsolutePosition. -xlinka
    public readonly Sync<float2> Scroll;
    public readonly Sync<float2> ScrollSensitivity;

    // A RectTransform sits at its 100x100 default until the canvas runs a layout pass. Applying scroll against
    // that default (excess = content - 100) could shove the content out of the also-stale clip rect. Reject a
    // viewport at/under this floor and wait for a real laid-out rect (the next cycle re-runs ApplyScroll). -xlinka
    private const float UnlaidViewportFloor = 100f;

    private float2 _pressPoint;
    private float2 _pressAbsolute;

    // Live scrollable excess (content - viewport, per axis) cached each ApplyScroll so AbsolutePosition can
    // convert pixels <-> normalized at drag/wheel time without re-reading the rects. -xlinka
    private float2 _excess;

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
        get => new float2(Scroll.Value.x * _excess.x, Scroll.Value.y * _excess.y);
        set => SetNormalized(new float2(
            _excess.x > 0f ? Clamp01(value.x / _excess.x) : Scroll.Value.x,
            _excess.y > 0f ? Clamp01(value.y / _excess.y) : Scroll.Value.y));
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
        AbsolutePosition = new float2(
            abs.x - axis.x * sensitivity.x,
            abs.y - axis.y * sensitivity.y * 24f);
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

        _excess = new float2(
            MathF.Max(0f, contentRect.width - viewportRect.width),
            MathF.Max(0f, contentRect.height - viewportRect.height));

        var norm = new float2(Clamp01(Scroll.Value.x), Clamp01(Scroll.Value.y));
        if (norm != Scroll.Value)
        {
            Scroll.Value = norm;
        }

        var absolute = new float2(norm.x * _excess.x, norm.y * _excess.y);

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
