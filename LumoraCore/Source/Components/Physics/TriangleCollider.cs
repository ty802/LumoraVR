// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components;

/// <summary>
/// Single-triangle collider defined by three local-space points (double-sided).
/// </summary>
[ComponentCategory("Physics/Colliders")]
public class TriangleCollider : Collider
{
    public readonly Sync<float3> A;
    public readonly Sync<float3> B;
    public readonly Sync<float3> C;

    public TriangleCollider()
    {
        A = new Sync<float3>(this, new float3(-1f, -1f, 0f));
        B = new Sync<float3>(this, new float3(0f, 1f, 0f));
        C = new Sync<float3>(this, new float3(1f, -1f, 0f));
    }

    public override void OnAwake()
    {
        base.OnAwake();
        A.OnChanged += _ => RunApplyChanges();
        B.OnChanged += _ => RunApplyChanges();
        C.OnChanged += _ => RunApplyChanges();
    }

    public override BoundingBox GetLocalBounds()
    {
        float3 a = A.Value, b = B.Value, c = C.Value;
        var min = new float3(
            System.MathF.Min(a.x, System.MathF.Min(b.x, c.x)),
            System.MathF.Min(a.y, System.MathF.Min(b.y, c.y)),
            System.MathF.Min(a.z, System.MathF.Min(b.z, c.z))) + Offset.Value;
        var max = new float3(
            System.MathF.Max(a.x, System.MathF.Max(b.x, c.x)),
            System.MathF.Max(a.y, System.MathF.Max(b.y, c.y)),
            System.MathF.Max(a.z, System.MathF.Max(b.z, c.z))) + Offset.Value;
        return new BoundingBox(min, max);
    }
}
