// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Shared base of the collider gizmos: every collider shape hangs off Collider.Offset, so the offset
// watch and the centre lookup live here instead of in each of the five.
public abstract class ColliderGizmo<T> : ComponentGizmo where T : Collider
{
    // Collider green, so a collider reads apart from a light or a camera at a glance.
    protected override color BaseTint => new(0.35f, 0.95f, 0.45f, 0.85f);

    protected T? Collider => TargetAs<T>();

    // In the target's local space.
    protected float3 Center => Collider?.Offset.Value ?? float3.Zero;

    protected override void CollectShapeFields(List<IChangeable> fields)
    {
        var collider = Collider;
        if (collider == null)
            return;
        fields.Add(collider.Offset);
        CollectExtentFields(collider, fields);
    }

    // Shape-specific fields on top of the offset every collider has.
    protected abstract void CollectExtentFields(T collider, List<IChangeable> fields);
}
