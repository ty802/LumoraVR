// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Cylinder cage along the local Y axis: two rims and four uprights, with a radius pad on the top rim
// and height pads on both caps.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(CylinderCollider))]
public class CylinderColliderGizmo : ColliderGizmo<CylinderCollider>
{
    private ExtentHandle? _radius;
    private ExtentHandle? _heightTop;
    private ExtentHandle? _heightBottom;

    protected override void CollectExtentFields(CylinderCollider collider, List<IChangeable> fields)
    {
        fields.Add(collider.Radius);
        fields.Add(collider.Height);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        wire.Cylinder(Center, collider.Radius.Value, collider.Height.Value);
    }

    protected override void BuildHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        _radius = AddHandle("Radius", float3.Right, collider.Radius, 1f, GizmoMaterialKind.AxisX, MinimumExtent);
        _radius.ValueName.Value = "Radius";
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
        float half = collider.Height.Value * 0.5f;
        PlaceHandle(_radius, center + new float3(collider.Radius.Value, half, 0f));
        PlaceHandle(_heightTop, center + new float3(0f, half, 0f));
        PlaceHandle(_heightBottom, center - new float3(0f, half, 0f));
    }

    private const float MinimumExtent = 0.001f;
}
