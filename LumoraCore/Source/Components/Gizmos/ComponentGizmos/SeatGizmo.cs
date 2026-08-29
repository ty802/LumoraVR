// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Components.Avatar;

namespace Lumora.Core.Components.Gizmos;

// Marker for the pose a seated user is put into: the seat plane, the up axis, and the direction the
// occupant ends up facing.
//
// A seat has no extent to draw - it is a pose, and getting the pose wrong is the entire failure mode
// (backwards, sideways, sunk into the mesh). So this draws exactly the pose and nothing else: a small
// plane at the anchor, an up line, and a forward arrow along -Z, which is where a seated body faces.
// No pads, because there is no number here - the seat is edited by moving its slot, which the slot
// gizmo already does. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(Seat))]
public class SeatGizmo : ComponentGizmo
{
    // Teal, to read apart from the amber interaction chrome.
    protected override color BaseTint => new(0.4f, 0.9f, 0.8f, 0.85f);

    // A seat's marker is a fixed size in the seat's own space, so nothing here changes it.
    protected override void CollectShapeFields(List<IChangeable> fields)
    {
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<Seat>() == null)
            return;

        // The plane the occupant's hips land on, in the seat's own XZ.
        var a = new float3(-PanHalf, 0f, -PanHalf);
        var b = new float3(PanHalf, 0f, -PanHalf);
        var c = new float3(PanHalf, 0f, PanHalf);
        var d = new float3(-PanHalf, 0f, PanHalf);
        wire.Line(a, b); wire.Line(b, c); wire.Line(c, d); wire.Line(d, a);
        wire.Line(a, c); wire.Line(b, d);

        // Up axis and facing, which is what tells a backwards seat from a right one at a glance.
        wire.Line(float3.Zero, float3.Up * UpLength);
        wire.Arrow(float3.Zero, float3.Backward, FacingLength, FacingLength * 0.25f);
    }

    private const float PanHalf = 0.2f;
    private const float UpLength = 0.5f;
    private const float FacingLength = 0.35f;
}
