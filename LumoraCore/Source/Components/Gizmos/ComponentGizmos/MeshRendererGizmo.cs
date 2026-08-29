// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Wire box around what ONE renderer draws, as opposed to the slot gizmo's box which unions the whole
// subtree. Useful for the case the union hides: which of the four renderers on this object is the one
// sticking out.
//
// No pads. A renderer's extent is its mesh, and the only way to change it from here would be to edit
// the slot's scale, which the slot gizmo already does properly. -xlinka
public abstract class RendererBoundsGizmo : ComponentGizmo
{
    protected override color BaseTint => new(1f, 0.65f, 0.25f, 0.85f);

    // The mesh a renderer points at can be swapped, and its bounds come from whatever it names.
    protected override bool ShapeFieldsVary => true;

    protected override void BuildWire(GizmoWireBuilder wire)
    {
        var target = TargetComponent;
        if (target == null)
            return;
        if (!SlotBoundsHelper.TryGetRendererLocalBounds(target, out var bounds))
            return; // mesh still loading, or none assigned: nothing honest to draw
        wire.Box(bounds);
    }
}

[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(MeshRenderer))]
public class MeshRendererGizmo : RendererBoundsGizmo
{
    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<MeshRenderer>() is not { } renderer)
            return;
        fields.Add(renderer.Mesh);
        if (renderer.Mesh.Target is { IsDestroyed: false } mesh)
            fields.Add(mesh);
    }
}

// Bounds of a skinned renderer. These are the BIND POSE bounds the asset carries: live deformation is
// not baked back into it, so a heavily posed avatar will sit outside its own box. Drawing the bind
// pose is still worth more than drawing nothing, and re-deriving it per frame from live bones is what
// the slot gizmo's animated-bounds clock exists to avoid. -xlinka
[ComponentCategory("Utility/Gizmos")]
[GizmoForComponent(typeof(SkinnedMeshRenderer))]
public class SkinnedMeshRendererGizmo : RendererBoundsGizmo
{
    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        if (TargetAs<SkinnedMeshRenderer>() is { } renderer)
            fields.Add(renderer.MeshAsset);
    }
}
