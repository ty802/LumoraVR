// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

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
