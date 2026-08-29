// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Components.Touch;

namespace Lumora.Core.Components.Gizmos;

// The button's travel: a line from the rest position along the press axis, ticked at the two
// thresholds, with a pad on the bottomed-out end that drags Depth.
//
// Both PressAxis and RestOffset are authored in the MOVING slot's parent space, not the button's own,
// so this gizmo follows that parent instead of the component's slot. Drawing it in the button's space
// would come out rotated by whatever local rotation the button carries, which for a button on a
// curved panel is most of them.
//
// The two thresholds are drawn because they are the part nobody can see: a button that fires early or
// refuses to release is almost always a threshold pair sitting too close together, and there is no
// other way to look at that. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(PlungerButton))]
public class PlungerButtonGizmo : ComponentGizmo
{
    private ExtentHandle? _depth;

    // Amber, the same family as the other interaction chrome.
    protected override color BaseTint => new(1f, 0.75f, 0.3f, 0.85f);

    // Travel is authored against the moving slot's parent.
    protected override Slot? FollowSlot
    {
        get
        {
            var button = TargetAs<PlungerButton>();
            var moving = button?.Plunger.Target ?? button?.Slot;
            return moving?.Parent ?? button?.Slot;
        }
    }

    // The moving slot can be repointed, which moves the whole drawing into a different parent.
    protected override bool ShapeFieldsVary => true;

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<PlungerButton>() is not { } button)
            return;
        fields.Add(button.PressAxis);
        fields.Add(button.Depth);
        fields.Add(button.RestOffset);
        fields.Add(button.PressThreshold);
        fields.Add(button.ReleaseThreshold);
        fields.Add(button.Plunger);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<PlungerButton>() is not { } button)
            return;

        var axis = Axis(button);
        float depth = MathF.Max(0f, button.Depth.Value);
        var rest = button.RestOffset.Value;
        var bottom = rest + axis * depth;

        wire.Line(rest, bottom);
        wire.Cross(rest, MarkerSize);
        wire.Cross(bottom, MarkerSize);

        // Threshold ticks across the travel, so the gap between press and release is a visible width.
        wire.Tint = new color(0.4f, 1f, 0.5f, 0.8f);
        Tick(wire, rest + axis * (depth * Fraction(button.PressThreshold.Value)), axis);
        wire.Tint = new color(1f, 0.45f, 0.4f, 0.8f);
        Tick(wire, rest + axis * (depth * Fraction(button.ReleaseThreshold.Value)), axis);
        wire.Tint = BaseTint;
    }

    protected override void BuildHandles()
    {
        if (TargetAs<PlungerButton>() is not { } button)
            return;
        _depth = AddHandle("Depth", Axis(button), button.Depth, 1f, GizmoMaterialKind.Center, MinimumDepth);
        _depth.ValueName.Value = "Depth";
    }

    protected override void LayoutHandles()
    {
        if (TargetAs<PlungerButton>() is not { } button)
            return;

        var axis = Axis(button);
        _depth!.LocalAxis.Value = axis;
        PlaceHandle(_depth, button.RestOffset.Value + axis * MathF.Max(0f, button.Depth.Value));
    }

    // A zero axis would make the travel a point; the button itself falls back the same way.
    private static float3 Axis(PlungerButton button)
    {
        var axis = button.PressAxis.Value;
        return axis.LengthSquared < 1e-10f ? float3.Forward : axis.Normalized;
    }

    private static float Fraction(float value) => System.Math.Clamp(value, 0f, 1f);

    private static void Tick(GizmoWireBuilder wire, in float3 point, in float3 axis)
    {
        // Two short bars across the travel line, in whichever pair of directions is not the axis.
        var helper = MathF.Abs(axis.y) < 0.9f ? float3.Up : float3.Right;
        var u = float3.Cross(helper, axis).Normalized * TickSize;
        var v = float3.Cross(axis, u.Normalized) * TickSize;
        wire.Line(point - u, point + u);
        wire.Line(point - v, point + v);
    }

    private const float MarkerSize = 0.004f;
    private const float TickSize = 0.008f;
    private const float MinimumDepth = 0.0005f;
}
