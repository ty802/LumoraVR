// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Cone cage for the collider: base disc at the bottom, apex at the top, both measured from the
// offset. Radius pad on the rim, height pad on the apex.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(ConeCollider))]
public class ConeColliderGizmo : ColliderGizmo<ConeCollider>
{
    private ExtentHandle? _radius;
    private ExtentHandle? _height;

    protected override void CollectExtentFields(ConeCollider collider, List<IChangeable> fields)
    {
        fields.Add(collider.Radius);
        fields.Add(collider.Height);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        float half = collider.Height.Value * 0.5f;
        // The collider centres the cone on the offset, so the apex is half a height up and the shape
        // is emitted downward from it.
        wire.Cone(Center + new float3(0f, half, 0f), float3.Down, collider.Radius.Value, collider.Height.Value);
    }

    protected override void BuildHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        _radius = AddHandle("Radius", float3.Right, collider.Radius, 1f, GizmoMaterialKind.AxisX, MinimumExtent);
        _radius.ValueName.Value = "Radius";
        _height = AddHandle("Height", float3.Up, collider.Height, 0.5f, GizmoMaterialKind.AxisY, MinimumExtent);
        _height.ValueName.Value = "Height";
    }

    protected override void LayoutHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        var center = Center;
        float half = collider.Height.Value * 0.5f;
        PlaceHandle(_radius, center + new float3(collider.Radius.Value, -half, 0f));
        PlaceHandle(_height, center + new float3(0f, half, 0f));
    }

    private const float MinimumExtent = 0.001f;
}
