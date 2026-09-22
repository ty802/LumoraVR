// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

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
