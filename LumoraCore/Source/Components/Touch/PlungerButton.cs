// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A button that physically sinks. The fingertip's own penetration becomes travel along PressAxis,
// the visual follows the travel, and two thresholds with a gap between them decide when it latches
// down and when it comes back up.
//
// The hysteresis is the whole point. A single threshold turns the natural tremor of a held hand into
// a burst of presses and releases, because the tip sits within a millimetre of the trip point and
// crosses it every frame. Pressing at one depth and releasing at a shallower one means the finger has
// to actually come back out before the button fires again. -xlinka
[ComponentCategory("Interaction/Touch")]
public class PlungerButton : TouchControl, ICustomInspectorUI
{
    // Direction the button travels when pressed, in its parent's local space.
    public readonly Sync<float3> PressAxis;

    // Full travel in metres from rest to bottomed out.
    public readonly Sync<float> Depth;

    // Fraction of travel that trips the press.
    public readonly Sync<float> PressThreshold;

    // Fraction of travel the button must rise back above to release. Keep it below the press threshold.
    public readonly Sync<float> ReleaseThreshold;

    // Latch instead of springing back. A second press clears it.
    public readonly Sync<bool> Hold;

    // Fraction of travel a latched button rests at, so a held button looks held.
    public readonly Sync<float> HoldDepthRatio;

    public readonly Sync<bool> IsPressed;

    public readonly Sync<bool> IsHovering;

    public readonly Sync<bool> IsHolding;

    // 0 at rest and 1 bottomed out.
    public readonly Sync<float> CurrentDepth;

    // This component's own slot when empty.
    public readonly SyncRef<Slot> Plunger;

    // Captured on attach.
    public readonly Sync<float3> RestOffset;

    // Text field driven as this button's caption. Also names it on the pointer.
    public readonly SyncRef<IField<string>> Label;

    // Runs once when the button goes down.
    public readonly SyncDelegate<TouchAction> Pressed;

    // Runs every frame the button is held down.
    public readonly SyncDelegate<TouchAction> Pressing;

    // Runs once when the button comes back up.
    public readonly SyncDelegate<TouchAction> Released;

    // Which slot RestOffset was measured against. Rebinding to a DIFFERENT slot re-measures; coming
    // back from a save must not, because by then the slot sits wherever the drive last left it and
    // re-measuring would bake the depressed position in as the new rest. -xlinka
    [HideInInspector]
    private readonly SyncRef<Slot> _restSource;

    [HideInInspector]
    private readonly FieldDrive<float3> _plungerPosition;

    private TouchAction? _localPressed;
    private TouchAction? _localPressing;
    private TouchAction? _localReleased;

    private Slot? _boundPlunger;

    public PlungerButton()
    {
        PressAxis = new Sync<float3>(this, float3.Forward);
        Depth = new Sync<float>(this, 0.025f);
        PressThreshold = new Sync<float>(this, 0.85f);
        ReleaseThreshold = new Sync<float>(this, 0.55f);
        Hold = new Sync<bool>(this, false);
        HoldDepthRatio = new Sync<float>(this, 0.5f);
        IsPressed = new Sync<bool>(this, false);
        IsHovering = new Sync<bool>(this, false);
        IsHolding = new Sync<bool>(this, false);
        CurrentDepth = new Sync<float>(this, 0f);
        Plunger = new SyncRef<Slot>(this);
        RestOffset = new Sync<float3>(this, float3.Zero);
        Label = new SyncRef<IField<string>>(this);
        Pressed = new SyncDelegate<TouchAction>(this);
        Pressing = new SyncDelegate<TouchAction>(this);
        Released = new SyncDelegate<TouchAction>(this);
        _restSource = new SyncRef<Slot>(this);
        _plungerPosition = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public string? LabelText
    {
        get => Label.Target?.Value;
        set
        {
            if (Label.Target != null)
                Label.Target.Value = value!;
        }
    }

    // True while the button is either physically down or latched.
    public bool IsDownOrHeld => IsPressed.Value || IsHolding.Value;

    protected override string? PointerLabel => LabelText ?? base.PointerLabel;

    // A component method replicates; a closure stays local.
    public void SetPressedAction(TouchAction? action) => Bind(Pressed, ref _localPressed, action);

    public void SetPressingAction(TouchAction? action) => Bind(Pressing, ref _localPressing, action);

    public void SetReleasedAction(TouchAction? action) => Bind(Released, ref _localReleased, action);

    public override void OnAttach()
    {
        base.OnAttach();
        CaptureRestOffset();
    }

    public override void OnStart()
    {
        base.OnStart();
        Plunger.OnTargetChange += _ => EnsureDrive();
        EnsureDrive();
        ApplyVisual();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        EnsureDrive();
        ApplyVisual();
    }

    public override void OnDestroy()
    {
        _plungerPosition.DriveTarget(null);
        base.OnDestroy();
    }

    // Re-measure the rest position from where the moving slot sits right now. Run it after moving the
    // visual by hand, otherwise the button springs back to wherever it used to be.
    [SyncMethod]
    public void CaptureRestOffset()
    {
        var target = ResolvePlunger();
        if (target == null)
            return;

        RestOffset.Value = target.LocalPosition.Value;
        _restSource.Target = target;
    }

    // Reads back the DRIVE, not the fields: "driving X" is the answer to nine out of ten reports of a
    // button that does not visibly move, and it cannot be worked out from the members alone. -xlinka
    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Travel", $"{Clamp01(CurrentDepth.Value) * 100f:0}% of {Depth.Value * 100f:0.#} cm");
        InspectorStats.AddRow(ui, "Thresholds", $"press {Clamp01(PressThreshold.Value) * 100f:0}% / release {Clamp01(ReleaseThreshold.Value) * 100f:0}%");
        InspectorStats.AddRow(ui, "State", IsPressed.Value ? "pressed" : (IsHolding.Value ? "latched" : (IsHovering.Value ? "hovered" : "idle")));

        var plunger = ResolvePlunger();
        InspectorStats.AddRow(ui, "Drive", _plungerPosition.Target == null
            ? "not bound"
            : $"driving {plunger?.SlotName.Value ?? "?"} position");

        float3 rest = RestOffset.Value;
        InspectorStats.AddRow(ui, "Rest", $"{rest.x:0.###}, {rest.y:0.###}, {rest.z:0.###}");

        if (Clamp01(ReleaseThreshold.Value) >= Clamp01(PressThreshold.Value))
            InspectorStats.AddRow(ui, "Warning", "release threshold is not below press - the button will chatter");
    }

    protected override void OnTouchContact(in TouchContact contact)
    {
        if (contact.Hover == TouchPhase.Begin)
        {
            IsHovering.Value = true;
            PulseHover(in contact);
        }
        else if (contact.Hover == TouchPhase.End)
        {
            IsHovering.Value = false;
        }

        if (contact.Contact == TouchPhase.End || contact.Contact == TouchPhase.None)
        {
            if (contact.Contact == TouchPhase.End)
                ClearPress(in contact);
            return;
        }

        float travel = contact.Kind == TouchProbeKind.Remote
            ? 1f
            : MeasureTravel(in contact);

        CurrentDepth.Value = travel;

        float press = Clamp01(PressThreshold.Value);
        float release = Clamp01(ReleaseThreshold.Value);

        if (!IsPressed.Value)
        {
            if (travel >= press)
                RunPress(in contact);
        }
        else if (travel <= release)
        {
            RunRelease(in contact);
        }
        else
        {
            Run(Pressing, _localPressing, in contact);
        }
    }

    // Travel is measured against the tip, not against the surface point: the surface point stops at
    // the collider face and would peg travel at zero no matter how far the finger keeps going. -xlinka
    private float MeasureTravel(in TouchContact contact)
    {
        var target = ResolvePlunger();
        var frame = target?.Parent ?? target;
        if (frame == null)
            return 0f;

        float3 axis = PressAxis.Value.Normalized;
        if (axis.LengthSquared < 1e-8f)
            axis = float3.Forward;

        float depth = MathF.Max(Depth.Value, 1e-4f);
        float3 local = frame.GlobalPointToLocal(contact.Tip) - RestOffset.Value;
        return Clamp01(float3.Dot(axis, local) / depth);
    }

    private void RunPress(in TouchContact contact)
    {
        IsPressed.Value = true;

        // A latched button's next press is what UNLATCHES it, so the latch is checked before it is
        // set - otherwise the same press that clears it immediately re-arms it. -xlinka
        if (IsHolding.Value)
            IsHolding.Value = false;
        else if (Hold.Value)
            IsHolding.Value = true;

        PulseContact(in contact);
        Run(Pressed, _localPressed, in contact);
    }

    private void RunRelease(in TouchContact contact)
    {
        IsPressed.Value = false;
        PulseContact(in contact);
        Run(Released, _localReleased, in contact);
    }

    private void ClearPress(in TouchContact contact)
    {
        CurrentDepth.Value = 0f;
        if (IsPressed.Value)
            RunRelease(in contact);
    }

    private Slot? ResolvePlunger()
    {
        var target = Plunger.Target;
        if (target != null && !target.IsDestroyed)
            return target;
        return Slot != null && !Slot.IsDestroyed ? Slot : null;
    }

    private void EnsureDrive()
    {
        var target = ResolvePlunger();
        if (ReferenceEquals(target, _boundPlunger) && _plungerPosition.Target != null)
            return;

        _plungerPosition.DriveTarget(null);
        _boundPlunger = target;
        if (target == null)
            return;

        if (_restSource.Target != target)
            CaptureRestOffset();

        _plungerPosition.DriveTarget(target.LocalPosition);
    }

    private void ApplyVisual()
    {
        if (_plungerPosition.Target == null)
            return;

        float3 axis = PressAxis.Value.Normalized;
        if (axis.LengthSquared < 1e-8f)
            axis = float3.Forward;

        float resting = IsHolding.Value ? Clamp01(HoldDepthRatio.Value) : 0f;
        float travel = MathF.Max(Clamp01(CurrentDepth.Value), resting);

        _plungerPosition.SetValue(RestOffset.Value + axis * (Depth.Value * travel));
    }

    private static float Clamp01(float value)
        => value < 0f ? 0f : (value > 1f ? 1f : value);
}
