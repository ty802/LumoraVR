// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class Slider : InteractionElement
{
    public readonly Sync<float> Value;
    public readonly Sync<float> Min;
    public readonly Sync<float> Max;
    public readonly Sync<float> Power;
    public readonly Sync<float2> AnchorOffset;
    public FieldDrive<float2>? HandleAnchorMinDrive { get; private set; }
    public FieldDrive<float2>? HandleAnchorMaxDrive { get; private set; }

    public event Action<Slider, float>? ValueChanged;

    public Slider()
    {
        Value = new Sync<float>(this, 0f);
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
        Power = new Sync<float>(this, 1f);
        AnchorOffset = new Sync<float2>(this, new float2(0f, 0.5f));
    }

    public override void OnAwake()
    {
        base.OnAwake();
        HandleAnchorMinDrive = new FieldDrive<float2>(World);
        HandleAnchorMaxDrive = new FieldDrive<float2>(World);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateHandleDrives();
    }

    public override void OnDestroy()
    {
        HandleAnchorMinDrive?.Release();
        HandleAnchorMaxDrive?.Release();
        HandleAnchorMinDrive = null;
        HandleAnchorMaxDrive = null;
        base.OnDestroy();
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
        if (Power.Value > 0f && Power.Value != 1f)
        {
            t = MathF.Pow(t, 1f / Power.Value);
        }

        float min = Min.Value;
        float max = Max.Value;
        float value = min + (max - min) * t;
        if (Value.Value == value) return;

        Value.Value = value;
        UpdateHandleDrives();
        ValueChanged?.Invoke(this, value);
    }

    public void UpdateHandleDrives()
    {
        var anchor = GetHandleAnchor();
        if (HandleAnchorMinDrive?.IsLinkValid == true)
        {
            HandleAnchorMinDrive.SetValue(anchor);
        }
        if (HandleAnchorMaxDrive?.IsLinkValid == true)
        {
            HandleAnchorMaxDrive.SetValue(anchor);
        }
    }

    private float2 GetHandleAnchor()
    {
        float min = Min.Value;
        float max = Max.Value;
        float t = max > min ? (Value.Value - min) / (max - min) : 0f;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        float power = Power.Value;
        if (power > 0f && power != 1f)
        {
            t = MathF.Pow(t, power);
        }

        return new float2(t, 0f) + AnchorOffset.Value;
    }
}
