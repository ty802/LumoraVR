// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Wire box at the collider's offset, with a pad on each of the six faces. Opposite faces drive the
// same size component, each along its own outward axis, so pulling either one widens the box in both
// directions about the offset - which is what the collider actually does, and pretending otherwise
// would make one of the two pads lie.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(BoxCollider))]
public class BoxColliderGizmo : ColliderGizmo<BoxCollider>
{
    private ExtentHandle? _xPositive;
    private ExtentHandle? _xNegative;
    private ExtentHandle? _yPositive;
    private ExtentHandle? _yNegative;
    private ExtentHandle? _zPositive;
    private ExtentHandle? _zNegative;

    protected override void CollectExtentFields(BoxCollider collider, List<IChangeable> fields)
        => fields.Add(collider.Size);

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        wire.Box(Center, collider.Size.Value);
    }

    protected override void BuildHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        // Half-extent per unit of Size: the face sits at Size/2 from the centre.
        _xPositive = AddHandle("SizeX+", float3.Right, collider.Size, 0, 0.5f, GizmoMaterialKind.AxisX, MinimumSize);
        _xNegative = AddHandle("SizeX-", float3.Left, collider.Size, 0, 0.5f, GizmoMaterialKind.AxisX, MinimumSize);
        _yPositive = AddHandle("SizeY+", float3.Up, collider.Size, 1, 0.5f, GizmoMaterialKind.AxisY, MinimumSize);
        _yNegative = AddHandle("SizeY-", float3.Down, collider.Size, 1, 0.5f, GizmoMaterialKind.AxisY, MinimumSize);
        _zPositive = AddHandle("SizeZ+", float3.Forward, collider.Size, 2, 0.5f, GizmoMaterialKind.AxisZ, MinimumSize);
        _zNegative = AddHandle("SizeZ-", float3.Backward, collider.Size, 2, 0.5f, GizmoMaterialKind.AxisZ, MinimumSize);

        foreach (var handle in Handles)
            handle.ValueName.Value = "Size";
    }

    protected override void LayoutHandles()
    {
        var collider = Collider;
        if (collider == null)
            return;

        var center = Center;
        var half = collider.Size.Value * 0.5f;
        PlaceHandle(_xPositive, center + new float3(half.x, 0f, 0f));
        PlaceHandle(_xNegative, center - new float3(half.x, 0f, 0f));
        PlaceHandle(_yPositive, center + new float3(0f, half.y, 0f));
        PlaceHandle(_yNegative, center - new float3(0f, half.y, 0f));
        PlaceHandle(_zPositive, center + new float3(0f, 0f, half.z));
        PlaceHandle(_zNegative, center - new float3(0f, 0f, half.z));
    }

    // A zero-thickness box is a shape the physics side has to guess about, so the pads stop short.
    private const float MinimumSize = 0.001f;
}
