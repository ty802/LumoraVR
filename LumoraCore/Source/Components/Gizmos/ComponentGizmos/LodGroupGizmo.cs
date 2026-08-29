// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// One ring per LOD level at the distance that level stops being used, in the horizontal plane through
// the group, plus a pad per level on the ring. Each level gets its own shade so which ring belongs to
// which row is readable without labels.
//
// Drawn in WORLD metres, not the group's local units, so this opts out of the scale mirror: a LOD
// switch distance is already a real distance, and the group's own scale is folded into it by
// DistanceScale together with the engine's LOD bias. Re-applying the slot scale on top would put every
// ring in the wrong place, and a non-uniformly scaled group would draw ellipses for what is a sphere
// of viewer distance.
//
// Rings lie flat rather than being drawn as spheres: a stack of four concentric spheres around an
// object is unreadable, and the number that matters is a radius you can pace out. The pads therefore
// run along +X, which is the one place a ring is guaranteed not to be edge-on to itself. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(LodGroup))]
public class LodGroupGizmo : ComponentGizmo
{
    private readonly List<ExtentHandle> _levelHandles = new();

    protected override bool ShapeUsesTargetScale => false;

    // Levels come and go, and each carries its own distance field.
    protected override bool ShapeFieldsVary => true;

    protected override color BaseTint => new(0.85f, 0.75f, 1f, 0.8f);

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<LodGroup>() is not { } group)
            return;
        // The level LIST itself carries no change event - sync lists do not raise one and do not mark
        // their owner - so only the per-level distances can be watched here. A level added or removed
        // therefore reaches the rings on the group's next change rather than immediately, which is the
        // honest limit of what the datamodel offers today. -xlinka
        fields.Add(group.CullBeyondLast);
        fields.Add(group.IgnoreScale);
        for (int i = 0; i < group.Levels.Count; i++)
        {
            if (group.Levels[i] is { } level)
                fields.Add(level.Distance);
        }
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        if (TargetAs<LodGroup>() is not { } group)
            return;

        int count = group.Levels.Count;
        for (int i = 0; i < count; i++)
        {
            var (_, end) = group.BandFor(i);
            if (end <= 0f)
                continue; // the last band runs for ever when nothing is culled beyond it

            wire.Tint = ShadeFor(i, count);
            wire.Circle(float3.Zero, float3.Up, end, RingSegments);
            // A short riser at the pad's bearing, so a ring lying in the floor is still findable when
            // the viewer is standing on it.
            wire.Line(new float3(end, 0f, 0f), new float3(end, RiserHeight, 0f));
        }
        wire.Tint = BaseTint;
    }

    protected override void BuildHandles() => RebuildLevelHandles();

    protected override void LayoutHandles()
    {
        if (TargetAs<LodGroup>() is not { } group)
            return;

        RebuildLevelHandles();

        float scale = group.DistanceScale;
        int count = group.Levels.Count;
        for (int i = 0; i < _levelHandles.Count; i++)
        {
            var handle = _levelHandles[i];
            if (i >= count || group.Levels[i] is not { } level)
            {
                HideHandle(handle);
                continue;
            }

            // The pad drags the AUTHORED distance, but sits at the scaled one, so the ratio between the
            // two is what a hand movement has to be divided by.
            handle.FloatField.Target = level.Distance;
            handle.DistancePerUnit.Value = MathF.Max(scale, 1e-4f);
            PlaceHandle(handle, new float3(MathF.Max(0f, level.Distance.Value) * scale, RiserHeight, 0f));
        }
    }

    // Pads are built up to a fixed ceiling and shown per level, rather than created and destroyed as
    // levels are added: a slot rebuild mid-drag would drop the drag, and eight is already more levels
    // than anything sane authors.
    private void RebuildLevelHandles()
    {
        if (TargetAs<LodGroup>() is not { } group)
            return;

        int wanted = System.Math.Min(group.Levels.Count, MaxLevelHandles);
        for (int i = _levelHandles.Count; i < wanted; i++)
        {
            var handle = AddHandle($"Level{i}", float3.Right, (IField<float>?)null, 1f,
                GizmoMaterialKind.Center, MinimumDistance);
            handle.ValueName.Value = "LOD Distance";
            _levelHandles.Add(handle);
        }
    }

    // Later levels read dimmer, which matches what they are: the ones that only matter far away.
    private static color ShadeFor(int index, int count)
    {
        float t = count <= 1 ? 0f : index / (float)(count - 1);
        return new color(0.95f - 0.35f * t, 0.8f - 0.2f * t, 1f, 0.85f - 0.25f * t);
    }

    private const int RingSegments = 48;
    private const float RiserHeight = 0.15f;
    private const float MinimumDistance = 0.01f;
    private const int MaxLevelHandles = 8;
}

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
