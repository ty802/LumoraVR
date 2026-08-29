// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Components.Magnets;

namespace Lumora.Core.Components.Gizmos;

// The socket's catch radius as a sphere, with a pad on it, plus a short arrow for the pose an item
// snaps to.
//
// MaxAngle is deliberately not drawn. It is a limit on the ROTATION difference between the item and
// the socket, not a direction cone, and drawing it as a cone would invent a directional restriction
// the socket does not have - which is worse than leaving it to the number in the inspector. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(MagnetSocket))]
public class MagnetSocketGizmo : ComponentGizmo
{
    private ExtentHandle? _reach;

    // Magenta, the colour the magnet system already reads as.
    protected override color BaseTint => new(1f, 0.45f, 0.75f, 0.85f);

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<MagnetSocket>() is { } socket)
            fields.Add(socket.MaxDistance);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<MagnetSocket>() is not { } socket)
            return;

        float reach = System.Math.Max(0f, socket.MaxDistance.Value);
        if (reach > 0f)
            wire.Sphere(float3.Zero, reach, segments: 24);

        // Which way up an item lands, drawn against the reach so the two read as one socket.
        float marker = reach > 0f ? reach : DefaultMarker;
        wire.Arrow(float3.Zero, float3.Backward, marker, marker * 0.25f);
        wire.Line(float3.Zero, float3.Up * marker * 0.6f);
    }

    protected override void BuildHandles()
    {
        if (TargetAs<MagnetSocket>() is not { } socket)
            return;
        _reach = AddHandle("MaxDistance", float3.Right, socket.MaxDistance, 1f, GizmoMaterialKind.AxisX, 0f);
        _reach.ValueName.Value = "Max Distance";
    }

    protected override void LayoutHandles()
    {
        if (TargetAs<MagnetSocket>() is not { } socket)
            return;
        PlaceHandle(_reach, new float3(System.Math.Max(0f, socket.MaxDistance.Value), 0f, 0f));
    }

    private const float DefaultMarker = 0.05f;
}
