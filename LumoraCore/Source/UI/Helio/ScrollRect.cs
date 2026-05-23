// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class ScrollRect : InteractionElement
{
    public readonly Sync<float2> Scroll;
    public readonly Sync<float2> ScrollSensitivity;

    private float2 _pressPoint;
    private float2 _pressScroll;

    public event Action<ScrollRect, float2>? ScrollChanged;

    public ScrollRect()
    {
        Scroll = new Sync<float2>(this, float2.Zero);
        ScrollSensitivity = new Sync<float2>(this, float2.One);
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
        var value = new float2(
            _pressScroll.x - delta.x * sensitivity.x,
            _pressScroll.y - delta.y * sensitivity.y);

        if (Scroll.Value == value) return;

        Scroll.Value = value;
        ScrollChanged?.Invoke(this, value);
    }
}
