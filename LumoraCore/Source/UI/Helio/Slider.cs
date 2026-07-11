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
    // Handle/fill anchor drives. Declared members: the targets replicate and save.
    public readonly FieldDrive<float2> HandleAnchorMinDrive = new();
    public readonly FieldDrive<float2> HandleAnchorMaxDrive = new();
    // Drives the filled-track portion's AnchorMax so the track shows progress.
    public readonly FieldDrive<float2> FillAnchorMaxDrive = new();

    // Duplicable change action - see Button.Pressed.
    public readonly SyncDelegate<Action<Slider, float>> ChangeAction;

    public event Action<Slider, float>? ValueChanged;

    public Slider()
    {
        Value = new Sync<float>(this, 0f);
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
        Power = new Sync<float>(this, 1f);
        AnchorOffset = new Sync<float2>(this, new float2(0f, 0.5f));
        ChangeAction = new SyncDelegate<Action<Slider, float>>(this);
    }

    public void SetAction(Action<Slider, float>? action)
    {
        if (action == null)
            return;
        if (action.Target is IWorldElement)
            ChangeAction.Target = action;
        else
            ValueChanged += action;
    }

    public override void OnStart()
    {
        base.OnStart();
        RebindVisuals();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateHandleDrives();
    }

    // Default the handle/fill drives from the built child structure when nothing named a target. The
    // UIBuilder wires these at build time and the links now replicate and persist, so this only fills
    // in for a slider built by hand or duplicated before a link was stored.
    private void RebindVisuals()
    {
        if (Slot == null)
            return;

        if (HandleAnchorMinDrive.ShouldApplyDefault || HandleAnchorMaxDrive.ShouldApplyDefault)
        {
            var handle = Slot.FindChild("HandleArea", recursive: false)?.FindChild("Handle", recursive: false)?.GetComponent<RectTransform>();
            if (handle != null)
            {
                if (HandleAnchorMinDrive.ShouldApplyDefault)
                    HandleAnchorMinDrive.DriveTarget(handle.AnchorMin);
                if (HandleAnchorMaxDrive.ShouldApplyDefault)
                    HandleAnchorMaxDrive.DriveTarget(handle.AnchorMax);
            }
        }

        if (FillAnchorMaxDrive.ShouldApplyDefault)
        {
            var fill = Slot.FindChild("Track", recursive: false)?.FindChild("Fill", recursive: false)?.GetComponent<RectTransform>();
            if (fill != null)
                FillAnchorMaxDrive.DriveTarget(fill.AnchorMax);
        }

        UpdateHandleDrives();
    }

    // Horizontal slider: keep horizontal drags (that's the value), but hand a clearly-vertical drag to a
    // scrolling ancestor so the slider doesn't trap a scroll gesture.
    public override bool PassDragToParent(in float2 dragDelta)
        => System.Math.Abs(dragDelta.y) > DragPassThreshold && System.Math.Abs(dragDelta.y) > System.Math.Abs(dragDelta.x);

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

        // PointIn, not LocalPoint: a slider inside a scrolled list draws where its chunk offset puts it, so
        // the raw canvas point would read against the rect's unscrolled position and jump the value. -xlinka
        var point = context.PointIn(Slot);
        float t = (point.x - rect.Value.xMin) / rect.Value.width;
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
        ChangeAction.Target?.Invoke(this, value);
    }

    public void UpdateHandleDrives()
    {
        var anchor = GetHandleAnchor();
        if (HandleAnchorMinDrive.IsLinkValid)
        {
            HandleAnchorMinDrive.SetValue(anchor);
        }
        if (HandleAnchorMaxDrive.IsLinkValid)
        {
            HandleAnchorMaxDrive.SetValue(anchor);
        }
        if (FillAnchorMaxDrive.IsLinkValid)
        {
            FillAnchorMaxDrive.SetValue(new float2(anchor.x - AnchorOffset.Value.x, 1f));
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
