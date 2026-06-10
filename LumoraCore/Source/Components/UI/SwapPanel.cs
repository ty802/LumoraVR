// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public enum SwapDirection
{
    None,
    Forward,
    Back,
}

public class SwapPanel : UIComponent
{
    public readonly Sync<float> Duration;

    private Slot? _containerSlot;
    private Slot? _currentPageSlot;
    private readonly List<SlideAnim> _anims = new();

    private struct SlideAnim
    {
        public RectTransform Rect;
        public float2 FromAnchorMin;
        public float2 FromAnchorMax;
        public float2 ToAnchorMin;
        public float2 ToAnchorMax;
        public float Elapsed;
        public float Duration;
        public bool DestroyOnEnd;
        public Slot Slot;
    }

    public Slot? ContainerSlot
    {
        get
        {
            EnsureContainer();
            return _containerSlot;
        }
    }

    public Slot? CurrentPageSlot => _currentPageSlot;

    public SwapPanel()
    {
        Duration = new Sync<float>(this, 0.25f);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureContainer();
    }

    public UIBuilder Show(Action<UIBuilder> build, SwapDirection direction = SwapDirection.None)
    {
        EnsureContainer();

        var oldPage = _currentPageSlot;
        var page = _containerSlot!.AddSlot(direction == SwapDirection.Back ? "PageBack" : "Page");
        var rect = page.AttachComponent<RectTransform>();

        if (direction == SwapDirection.None || Duration.Value <= 0f)
        {
            Fill(rect);
            _currentPageSlot = page;
            var builder = new UIBuilder(page);
            build(builder);
            oldPage?.Destroy();
            return builder;
        }

        float dur = Duration.Value;

        // New page slides in from the right (Forward) or left (Back).
        var newFromMin = direction == SwapDirection.Forward ? new float2(1f, 0f) : new float2(-1f, 0f);
        var newFromMax = direction == SwapDirection.Forward ? new float2(2f, 1f) : new float2(0f, 1f);
        SetAnchors(rect, newFromMin, newFromMax);
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;

        _anims.Add(new SlideAnim
        {
            Rect = rect,
            FromAnchorMin = newFromMin,
            FromAnchorMax = newFromMax,
            ToAnchorMin = float2.Zero,
            ToAnchorMax = float2.One,
            Elapsed = 0f,
            Duration = dur,
            DestroyOnEnd = false,
            Slot = page,
        });

        if (oldPage != null)
        {
            var oldRect = oldPage.GetComponent<RectTransform>();
            if (oldRect != null)
            {
                var oldToMin = direction == SwapDirection.Forward ? new float2(-1f, 0f) : new float2(1f, 0f);
                var oldToMax = direction == SwapDirection.Forward ? new float2(0f, 1f) : new float2(2f, 1f);
                _anims.Add(new SlideAnim
                {
                    Rect = oldRect,
                    FromAnchorMin = oldRect.AnchorMin.Value,
                    FromAnchorMax = oldRect.AnchorMax.Value,
                    ToAnchorMin = oldToMin,
                    ToAnchorMax = oldToMax,
                    Elapsed = 0f,
                    Duration = dur,
                    DestroyOnEnd = true,
                    Slot = oldPage,
                });
            }
            else
            {
                oldPage.Destroy();
            }
        }

        _currentPageSlot = page;
        var b = new UIBuilder(page);
        build(b);
        return b;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_anims.Count == 0) return;

        for (int i = _anims.Count - 1; i >= 0; i--)
        {
            var a = _anims[i];
            a.Elapsed += delta;
            float t = a.Duration > 0f ? MathF.Min(1f, a.Elapsed / a.Duration) : 1f;
            float e = Ease(t);

            if (!a.Rect.IsDestroyed)
            {
                var min = float2.Lerp(a.FromAnchorMin, a.ToAnchorMin, e);
                var max = float2.Lerp(a.FromAnchorMax, a.ToAnchorMax, e);
                SetAnchors(a.Rect, min, max);
            }

            if (t >= 1f)
            {
                if (a.DestroyOnEnd && !a.Slot.IsDestroyed) a.Slot.Destroy();
                _anims.RemoveAt(i);
            }
            else
            {
                _anims[i] = a;
            }
        }
    }

    public void Clear()
    {
        for (int i = _anims.Count - 1; i >= 0; i--)
        {
            var a = _anims[i];
            if (a.DestroyOnEnd && !a.Slot.IsDestroyed) a.Slot.Destroy();
        }
        _anims.Clear();
        _currentPageSlot?.Destroy();
        _currentPageSlot = null;
    }

    private void EnsureContainer()
    {
        if (_containerSlot != null) return;

        _containerSlot = Slot.AddSlot("Pages");
        Fill(_containerSlot.AttachComponent<RectTransform>());
    }

    // cubic ease-in-out — softer than linear, no overshoot. - xlinka
    private static float Ease(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    private static void SetAnchors(RectTransform rect, float2 min, float2 max)
    {
        if (rect.AnchorMin.Value != min) rect.AnchorMin.Value = min;
        if (rect.AnchorMax.Value != max) rect.AnchorMax.Value = max;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
