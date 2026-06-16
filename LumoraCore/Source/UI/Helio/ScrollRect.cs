// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class ScrollRect : InteractionElement, IUIAxisActionReceiver
{
    public readonly SyncRef<RectTransform> Content;
    public readonly Sync<float2> Scroll;
    public readonly Sync<float2> ScrollSensitivity;

    private float2 _pressPoint;
    private float2 _pressScroll;

    public event Action<ScrollRect, float2>? ScrollChanged;

    public ScrollRect()
    {
        Content = new SyncRef<RectTransform>(this);
        Scroll = new Sync<float2>(this, float2.Zero);
        ScrollSensitivity = new Sync<float2>(this, float2.One);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // The scroll offset is applied during the canvas rebuild (Canvas.ApplyScrollRects).
        // Changing Scroll alone touches no RectTransform, so nothing would signal the canvas
        // dirty and the rebuild - and thus the scroll - would never happen. This is a layout
        // change (content repositions), so the rects must be recomputed and every nested chunk
        // re-rendered at its scrolled position.
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
        _pressScroll = Scroll.Value;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        var delta = context.LocalPoint - _pressPoint;
        var sensitivity = ScrollSensitivity.Value;
        SetScroll(new float2(
            _pressScroll.x - delta.x * sensitivity.x,
            _pressScroll.y - delta.y * sensitivity.y));
    }

    public bool ProcessAxis(in UIInteractionContext context, in float2 axis)
    {
        if (axis == float2.Zero)
        {
            return false;
        }

        var sensitivity = ScrollSensitivity.Value;
        var value = new float2(
            Scroll.Value.x - axis.x * sensitivity.x,
            Scroll.Value.y - axis.y * sensitivity.y * 24f);
        return SetScroll(value);
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

        var maxScroll = new float2(
            MathF.Max(0f, contentRect.width - viewportRect.width),
            MathF.Max(0f, contentRect.height - viewportRect.height));
        var requested = Scroll.Value;
        var clamped = new float2(
            Clamp(requested.x, 0f, maxScroll.x),
            Clamp(requested.y, 0f, maxScroll.y));

        if (requested != clamped)
        {
            Scroll.Value = clamped;
        }

        if (clamped == float2.Zero)
        {
            return false;
        }

        content.SetLocalComputeRect(new Rect(
            contentRect.x - clamped.x,
            contentRect.y + clamped.y,
            contentRect.width,
            contentRect.height));
        return true;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private bool SetScroll(float2 value)
    {
        if (Scroll.Value == value) return false;

        Scroll.Value = value;
        ScrollChanged?.Invoke(this, value);
        // Setting Scroll doesn't touch a RectTransform, and OnChanges isn't reliably raised for
        // it, so dirty the canvas directly - otherwise the rebuild that applies the scroll (and
        // thus the scroll itself) never runs. This is what makes the wheel and drag actually move.
        FindCanvas()?.MarkLayoutDirty();
        return true;
    }
}
