// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Wire bounds of a collider whose shape comes from a mesh. No pads: there is no number to drag - the
// extent is whatever geometry was handed to it, and the honest thing to draw is the box that geometry
// occupies rather than a shape that implies it can be resized here.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(MeshCollider))]
public class MeshColliderGizmo : ColliderGizmo<MeshCollider>
{
    // The mesh ref can be repointed at any time, and the bounds come from whatever it names.
    protected override bool ShapeFieldsVary => true;

    protected override void CollectExtentFields(MeshCollider collider, List<IChangeable> fields)
    {
        fields.Add(collider.Mesh);
        if (collider.Mesh.Target is { IsDestroyed: false } mesh)
            fields.Add(mesh);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        var bounds = collider.GetLocalBounds();
        if (bounds.Min.x > bounds.Max.x || bounds.Min.y > bounds.Max.y || bounds.Min.z > bounds.Max.z)
            return; // no mesh yet, or an empty one: draw nothing rather than a box at the origin
        wire.Box(bounds);
    }
}

// Wire bounds of a convex hull collider. Same reasoning as the mesh one: the hull is generated from a
// point set, so there is no extent field a pad could edit.
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(ConvexHullCollider))]
public class ConvexHullColliderGizmo : ColliderGizmo<ConvexHullCollider>
{
    protected override bool ShapeFieldsVary => true;

    protected override void CollectExtentFields(ConvexHullCollider collider, List<IChangeable> fields)
    {
        // Points is a sync list and raises no change event, so an edited hull redraws on the next
        // change to the collider rather than on the point itself. -xlinka
        fields.Add(collider.Mesh);
        fields.Add(collider.MinPointDistance);
        if (collider.Mesh.Target is { IsDestroyed: false } mesh)
            fields.Add(mesh);
    }

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var collider = Collider;
        if (collider == null)
            return;
        var bounds = collider.GetLocalBounds();
        if (bounds.Min.x > bounds.Max.x || bounds.Min.y > bounds.Max.y || bounds.Min.z > bounds.Max.z)
            return;
        wire.Box(bounds);
    }
}
