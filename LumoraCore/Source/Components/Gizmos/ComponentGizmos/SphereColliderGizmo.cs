// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Three great circles at the collider's radius, with a pad on each axis. Three pads rather than one
// because a sphere seen from anywhere has at least one of them side-on to the viewer, and a single
// pad would end up behind the shape half the time.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(SphereCollider))]
public class SphereColliderGizmo : ColliderGizmo<SphereCollider>
{
    private ExtentHandle? _x;
    private ExtentHandle? _y;
    private ExtentHandle? _z;

    protected override void CollectExtentFields(SphereCollider collider, List<IChangeable> fields)
        => fields.Add(collider.Radius);

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        wire.Sphere(Center, collider.Radius.Value);
    }

    protected override void BuildHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        _x = AddHandle("RadiusX", float3.Right, collider.Radius, 1f, GizmoMaterialKind.AxisX, MinimumRadius);
        _y = AddHandle("RadiusY", float3.Up, collider.Radius, 1f, GizmoMaterialKind.AxisY, MinimumRadius);
        _z = AddHandle("RadiusZ", float3.Forward, collider.Radius, 1f, GizmoMaterialKind.AxisZ, MinimumRadius);

        foreach (var handle in Handles)
            handle.ValueName.Value = "Radius";
    }

    protected override void LayoutHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        var center = Center;
        float radius = collider.Radius.Value;
        PlaceHandle(_x, center + new float3(radius, 0f, 0f));
        PlaceHandle(_y, center + new float3(0f, radius, 0f));
        PlaceHandle(_z, center + new float3(0f, 0f, radius));
    }

    private const float MinimumRadius = 0.001f;
}
