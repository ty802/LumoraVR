// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Capsule cage along the local Y axis: two rims, four uprights and four cap arcs, with radius pads on
// X and Z and height pads on both ends.
//
// Height is the full tip-to-tip extent, caps included, which is the number the collider itself
// carries. The height pads therefore ride the tips and not the rim of the straight section, so a
// capsule dragged to its minimum reads as a sphere rather than collapsing through itself. Radius is
// floored below half the height for the same reason. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(CapsuleCollider))]
public class CapsuleColliderGizmo : ColliderGizmo<CapsuleCollider>
{
    private ExtentHandle? _radiusX;
    private ExtentHandle? _radiusZ;
    private ExtentHandle? _heightTop;
    private ExtentHandle? _heightBottom;

    protected override void CollectExtentFields(CapsuleCollider collider, List<IChangeable> fields)
    {
        fields.Add(collider.Radius);
        fields.Add(collider.Height);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        wire.Capsule(Center, collider.Radius.Value, collider.Height.Value);
    }

    protected override void BuildHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        _radiusX = AddHandle("RadiusX", float3.Right, collider.Radius, 1f, GizmoMaterialKind.AxisX, MinimumExtent);
        _radiusZ = AddHandle("RadiusZ", float3.Forward, collider.Radius, 1f, GizmoMaterialKind.AxisZ, MinimumExtent);
        _radiusX.ValueName.Value = "Radius";
        _radiusZ.ValueName.Value = "Radius";

        _heightTop = AddHandle("HeightTop", float3.Up, collider.Height, 0.5f, GizmoMaterialKind.AxisY, MinimumExtent);
        _heightBottom = AddHandle("HeightBottom", float3.Down, collider.Height, 0.5f, GizmoMaterialKind.AxisY, MinimumExtent);
        _heightTop.ValueName.Value = "Height";
        _heightBottom.ValueName.Value = "Height";
    }

    protected override void LayoutHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        var center = Center;
        float radius = collider.Radius.Value;
        float half = System.Math.Max(collider.Height.Value * 0.5f, radius);
        PlaceHandle(_radiusX, center + new float3(radius, 0f, 0f));
        PlaceHandle(_radiusZ, center + new float3(0f, 0f, radius));
        PlaceHandle(_heightTop, center + new float3(0f, half, 0f));
        PlaceHandle(_heightBottom, center - new float3(0f, half, 0f));
    }

    private const float MinimumExtent = 0.001f;
}
