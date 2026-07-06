// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components;

/// <summary>
/// Cone-shaped collider: base disc of Radius at the bottom, apex at the top, centered on Offset.
/// </summary>
[ComponentCategory("Physics/Colliders")]
public class ConeCollider : Collider
{
    public readonly Sync<float> Height;
    public readonly Sync<float> Radius;

    public ConeCollider()
    {
        Height = new Sync<float>(this, 1f);
        Radius = new Sync<float>(this, 0.5f);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Height.OnChanged += _ => RunApplyChanges();
        Radius.OnChanged += _ => RunApplyChanges();
    }

    public override BoundingBox GetLocalBounds()
    {
        var half = new float3(Radius.Value, Height.Value * 0.5f, Radius.Value);
        return new BoundingBox(Offset.Value - half, Offset.Value + half);
    }
}
