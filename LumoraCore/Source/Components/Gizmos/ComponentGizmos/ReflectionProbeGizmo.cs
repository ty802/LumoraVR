// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// The probe's influence box with a pad on each face, plus a tick at the capture point.
//
// Two separate things, drawn separately because they are separate: the box is centred on the slot and
// is what box projection reprojects against, while OriginOffset only moves where the cubemap is
// captured FROM. Drawing the box around the offset - which is the obvious mistake - would make every
// probe with an offset look like its influence had moved when it had not. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(ReflectionProbe))]
public class ReflectionProbeGizmo : ComponentGizmo
{
    private ExtentHandle? _xPositive;
    private ExtentHandle? _xNegative;
    private ExtentHandle? _yPositive;
    private ExtentHandle? _yNegative;
    private ExtentHandle? _zPositive;
    private ExtentHandle? _zNegative;

    // Pale cyan, close to a mirror.
    protected override color BaseTint => new(0.6f, 0.95f, 0.95f, 0.8f);

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<ReflectionProbe>() is not { } probe)
            return;
        fields.Add(probe.Size);
        fields.Add(probe.OriginOffset);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<ReflectionProbe>() is not { } probe)
            return;

        wire.Box(float3.Zero, probe.Size.Value);

        var origin = probe.OriginOffset.Value;
        wire.Cross(origin, CaptureMarkerSize);
        if (origin.LengthSquared > 1e-8f)
            wire.Line(float3.Zero, origin); // tether, so an offset capture point is not read as loose
    }

    protected override void BuildHandles()
    {
        if (TargetAs<ReflectionProbe>() is not { } probe)
            return;

        _xPositive = AddHandle("SizeX+", float3.Right, probe.Size, 0, 0.5f, GizmoMaterialKind.AxisX, MinimumSize);
        _xNegative = AddHandle("SizeX-", float3.Left, probe.Size, 0, 0.5f, GizmoMaterialKind.AxisX, MinimumSize);
        _yPositive = AddHandle("SizeY+", float3.Up, probe.Size, 1, 0.5f, GizmoMaterialKind.AxisY, MinimumSize);
        _yNegative = AddHandle("SizeY-", float3.Down, probe.Size, 1, 0.5f, GizmoMaterialKind.AxisY, MinimumSize);
        _zPositive = AddHandle("SizeZ+", float3.Forward, probe.Size, 2, 0.5f, GizmoMaterialKind.AxisZ, MinimumSize);
        _zNegative = AddHandle("SizeZ-", float3.Backward, probe.Size, 2, 0.5f, GizmoMaterialKind.AxisZ, MinimumSize);

        foreach (var handle in Handles)
            handle.ValueName.Value = "Size";
    }

    protected override void LayoutHandles()
    {
        if (TargetAs<ReflectionProbe>() is not { } probe)
            return;

        var half = probe.Size.Value * 0.5f;
        PlaceHandle(_xPositive, new float3(half.x, 0f, 0f));
        PlaceHandle(_xNegative, new float3(-half.x, 0f, 0f));
        PlaceHandle(_yPositive, new float3(0f, half.y, 0f));
        PlaceHandle(_yNegative, new float3(0f, -half.y, 0f));
        PlaceHandle(_zPositive, new float3(0f, 0f, half.z));
        PlaceHandle(_zNegative, new float3(0f, 0f, -half.z));
    }

    private const float MinimumSize = 0.01f;
    private const float CaptureMarkerSize = 0.05f;
}
