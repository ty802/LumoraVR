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

    // Anti-compounding: ApplyScroll overrides the content's computed rect to scroll it. On cycles where the
    // content's layout is clean, ComputeRects SKIPS re-deriving its rect from anchors, so the rect coming into
    // ApplyScroll is the (already-scrolled) value we wrote last time. Re-applying the offset to that compounds
    // it and the content marches off-screen. We remember the unscrolled base + exactly what we last wrote, so we
    // can detect the skip and always scroll from the base. -xlinka
    private Rect _scrollBaseRect;
    private Rect _lastWrittenRect;
    private bool _hasScrollBase;
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

    /// <summary>Normalized 0..1 scroll position (the persisted value), clamped on set.</summary>
    public float2 NormalizedPosition
    {
        get => Scroll.Value;
        set => SetNormalized(new float2(Clamp01(value.x), Clamp01(value.y)));
    }

    /// <summary>Scroll position in PIXELS = normalized * scrollable excess. Setting converts back to normalized
    /// against the live excess (a zero-excess axis keeps its value, so no divide-by-zero).</summary>
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

    // Apply the current scroll position. Render-offset path: MOVE the content chunk's transform directly (no
    // rebuild, no re-tessellation). Falls back to one full pass
    // only when render-offset is off or the content chunk isn't built yet (first scroll). -xlinka
    private void ApplyScrollNow()
    {
        var canvas = FindCanvas();
        if (canvas == null)
            return;
        if (Canvas.ScrollRenderOffset)
        {
            EnsureScrollSetup();
            // Content slides left/up as you scroll right/down (same sign as the rebuild path in ApplyScroll).
            var absolute = AbsolutePosition;
            if (_contentRoot != null && canvas.ApplyScrollOffset(_contentRoot, new float2(-absolute.x, absolute.y)))
                return;
        }
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
        _pressPoint = context.LocalPoint;
        _pressAbsolute = AbsolutePosition;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        var delta = context.LocalPoint - _pressPoint;
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

        if (Canvas.ScrollRenderOffset)
        {
            // Render-offset path: the content is its own chunk baked once; scrolling MOVES that chunk (no rect
            // mutation, no re-tessellation). Here (a structural rebuild) we just store the current offset so
            // ComputeChunk positions the freshly-baked chunk; live scrolling moves it via ApplyScrollOffset. The
            // rect is never touched, so return false and the caller skips ApplyLayout. -xlinka
            EnsureScrollSetup();
            if (_contentRoot != null)
                _contentRoot.RenderOffset = new float2(-absolute.x, absolute.y);
            return false;
        }

        // Resolve the unscrolled base. If the incoming rect is EXACTLY what we wrote last cycle, ComputeRects
        // skipped re-anchoring the clean content this cycle - reuse the stored base so the offset can't compound.
        // Otherwise the layout re-derived the rect (fresh, or the content resized), so adopt it as the new base.
        Rect baseRect = (_hasScrollBase && SameRect(contentRect, _lastWrittenRect)) ? _scrollBaseRect : contentRect;
        _scrollBaseRect = baseRect;
        _hasScrollBase = true;

        var scrolled = new Rect(
            baseRect.x - absolute.x,
            baseRect.y + absolute.y,
            baseRect.width,
            baseRect.height);

        // Already at the right place (no scroll, or the base already equals the target) - don't touch the rect
        // or force a needless re-layout.
        if (SameRect(scrolled, contentRect))
        {
            _lastWrittenRect = contentRect;
            return false;
        }

        content.SetLocalComputeRect(scrolled);
        _lastWrittenRect = scrolled;
        return true;
    }

    private static bool SameRect(in Rect a, in Rect b)
        => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;

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
