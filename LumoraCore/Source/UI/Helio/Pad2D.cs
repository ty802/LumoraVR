// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

// 2D analog of Slider: drag anywhere in the pad to set a normalized float2 in
// [0,1] per axis. x runs 0 (left) -> 1 (right), y runs 0 (bottom) -> 1 (top),
// so (0,0) is the bottom-left corner (LocalComputeRect is Y-up: yMin bottom,
// yMax top). A small Handle child tracks the value via anchor drives. -xlinka
public sealed class Pad2D : InteractionElement
{
    public readonly Sync<float2> Value;
    // Handle anchor drives. Declared members: the targets replicate and save.
    public readonly FieldDrive<float2> HandleAnchorMinDrive = new();
    public readonly FieldDrive<float2> HandleAnchorMaxDrive = new();

    // Duplicable change action - see Button.Pressed.
    public readonly SyncDelegate<Action<Pad2D, float2>> ChangeAction;

    public event Action<Pad2D, float2>? ValueChanged;

    public Pad2D()
    {
        Value = new Sync<float2>(this, float2.Zero);
        ChangeAction = new SyncDelegate<Action<Pad2D, float2>>(this);
    }

    public void SetAction(Action<Pad2D, float2>? action)
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

    // Default the handle drives from the built child structure when nothing named a target. The
    // UIBuilder wires these at build time and the links now replicate and persist, so this only fills
    // in for a pad built by hand or duplicated before a link was stored.
    private void RebindVisuals()
    {
        if (Slot == null)
            return;

        if (HandleAnchorMinDrive.ShouldApplyDefault || HandleAnchorMaxDrive.ShouldApplyDefault)
        {
            var handle = Slot.FindChild("Handle", recursive: false)?.GetComponent<RectTransform>();
            if (handle != null)
            {
                if (HandleAnchorMinDrive.ShouldApplyDefault)
                    HandleAnchorMinDrive.DriveTarget(handle.AnchorMin);
                if (HandleAnchorMaxDrive.ShouldApplyDefault)
                    HandleAnchorMaxDrive.DriveTarget(handle.AnchorMax);
            }
        }

        UpdateHandleDrives();
    }

    // A pad owns the whole drag - it maps every direction to a value, so it must
    // never hand the gesture to a scrolling ancestor.
    public override bool PassDragToParent(in float2 dragDelta) => false;

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
        if (!rect.HasValue || rect.Value.width <= 0f || rect.Value.height <= 0f) return;

        // PointIn, not LocalPoint - the rect is in layout space, the pointer is in canvas-visible space. -xlinka
        var point = context.PointIn(Slot);
        float x = (point.x - rect.Value.xMin) / rect.Value.width;
        float y = (point.y - rect.Value.yMin) / rect.Value.height;
        if (x < 0f) x = 0f;
        if (x > 1f) x = 1f;
        if (y < 0f) y = 0f;
        if (y > 1f) y = 1f;

        var value = new float2(x, y);
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
    }

    private float2 GetHandleAnchor()
    {
        float x = Value.Value.x;
        float y = Value.Value.y;
        if (x < 0f) x = 0f;
        if (x > 1f) x = 1f;
        if (y < 0f) y = 0f;
        if (y > 1f) y = 1f;
        return new float2(x, y);
    }
}
