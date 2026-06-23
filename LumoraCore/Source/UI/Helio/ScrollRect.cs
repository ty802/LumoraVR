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
        // The scroll offset is applied during the canvas rebuild (Canvas.ApplyScrollRects). Changing Scroll
        // alone touches no RectTransform, so dirty the canvas so the rebuild (and thus the scroll) runs.
        FindCanvas()?.MarkLayoutDirty();
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
            // Render-offset path: slide the content's own chunk instead of mutating its rect. The chunk slot
            // translates by this offset and the canvas counter-translates the clip so the viewport window stays
            // fixed. Direction matches the rect-mutation below (content moves left/up as you scroll right/down).
            // Returns false so the caller skips ApplyLayout - the rect is never touched, so there's nothing to
            // re-lay-out, which is the whole point (no per-tick relayout, no fitter to fight). -xlinka
            SetContentRenderOffset(content, new float2(-absolute.x, absolute.y));
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

    // Push the scroll offset onto the content's GraphicChunkRoot (added at build time when ScrollRenderOffset
    // is on). No-op if the content isn't its own chunk - then the render-offset feature simply doesn't engage
    // for this ScrollRect. -xlinka
    private static void SetContentRenderOffset(RectTransform content, float2 offset)
    {
        var root = content.Slot.GetComponent<GraphicChunkRoot>();
        if (root != null && root.RenderOffset != offset)
        {
            root.RenderOffset = offset;
        }
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
        // Setting Scroll doesn't touch a RectTransform, and OnChanges isn't reliably raised for it, so dirty
        // the canvas directly - otherwise the rebuild that applies the scroll never runs.
        FindCanvas()?.MarkLayoutDirty();
    }
}
