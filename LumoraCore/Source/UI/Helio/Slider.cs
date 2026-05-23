// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

public sealed class Slider : InteractionElement
{
    public readonly Sync<float> Value;
    public readonly Sync<float> Min;
    public readonly Sync<float> Max;

    public event Action<Slider, float>? ValueChanged;

    public Slider()
    {
        Value = new Sync<float>(this, 0f);
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
    }

    protected override void OnPress(in UIInteractionContext context)
    {
        SetValueFromPoint(in context);
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        SetValueFromPoint(in context);
    }

    private void SetValueFromPoint(in UIInteractionContext context)
    {
        var rect = RectTransform?.LocalComputeRect;
        if (!rect.HasValue || rect.Value.width <= 0f) return;

        float t = (context.LocalPoint.x - rect.Value.xMin) / rect.Value.width;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        float min = Min.Value;
        float max = Max.Value;
        float value = min + (max - min) * t;
        if (Value.Value == value) return;

        Value.Value = value;
        ValueChanged?.Invoke(this, value);
    }
}
