// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Single cull ring for a distance cull, drawn the same way the LOD group's are so the two read as the
// same kind of thing.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(LodDistanceCull))]
public class LodDistanceCullGizmo : ComponentGizmo
{
    private ExtentHandle? _distance;

    protected override bool ShapeUsesTargetScale => false;

    protected override color BaseTint => new(0.85f, 0.75f, 1f, 0.8f);

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<LodDistanceCull>() is not { } cull)
            return;
        fields.Add(cull.MaxDistance);
        fields.Add(cull.FadeMargin);
        fields.Add(cull.IgnoreScale);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<LodDistanceCull>() is not { } cull)
            return;

        // The component already exposes the scaled numbers, bias included. Re-deriving that rule here
        // would be a second copy of it that drifts the first time either one is touched.
        float distance = cull.ScaledDistance;
        if (distance <= 0f)
            return;

        wire.Circle(float3.Zero, float3.Up, distance, RingSegments);
        wire.Line(new float3(distance, 0f, 0f), new float3(distance, RiserHeight, 0f));

        // The fade band, dimmer, so where things START disappearing is visible too.
        float fade = cull.ScaledFade;
        if (fade <= 0f)
            return;
        wire.Tint = new color(0.7f, 0.6f, 0.9f, 0.45f);
        wire.Circle(float3.Zero, float3.Up, MathF.Max(distance - fade, 0.01f), RingSegments);
        wire.Tint = BaseTint;
    }

    protected override void BuildHandles()
    {
        if (TargetAs<LodDistanceCull>() is not { } cull)
            return;
        _distance = AddHandle("MaxDistance", float3.Right, cull.MaxDistance, 1f, GizmoMaterialKind.Center,
            MinimumDistance);
        _distance.ValueName.Value = "Max Distance";
    }

    protected override void LayoutHandles()
    {
        if (TargetAs<LodDistanceCull>() is not { } cull)
            return;

        // Ratio between the authored number and the drawn one, read back off the component so the pad
        // moves at the same rate the ring does whatever the scale rule turns out to be.
        float authored = MathF.Max(0f, cull.MaxDistance.Value);
        float scale = authored > 1e-4f ? cull.ScaledDistance / authored : 1f;
        _distance!.DistancePerUnit.Value = MathF.Max(scale, 1e-4f);
        PlaceHandle(_distance, new float3(cull.ScaledDistance, RiserHeight, 0f));
    }

    private const int RingSegments = 48;
    private const float RiserHeight = 0.15f;
    private const float MinimumDistance = 0.01f;
}
