// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A collider frozen into world space for one frame of particle tests.
//
// WHY THIS EXISTS. The obvious way to write the collision pass is "for each particle, ask each
// collider to push it out", and that is what it used to do - which meant every particle re-read the
// collider slot's world matrix and global scale. A twenty bone chain against five body colliders paid
// a hundred slot transform resolutions a frame to learn five answers that could not have changed
// mid-solve. Snapshotting once per frame turns the inner loop into arithmetic on a flat struct array,
// and the world AABB that comes with the snapshot is what lets a chain skip a collider it cannot
// possibly reach without touching the collider at all. -xlinka
public struct DynamicBoneColliderShape
{
    public enum ShapeKind : byte
    {
        Sphere,
        Capsule,
        Box,
    }

    public ShapeKind Kind;

    // Sphere: centre. Capsule: first end. Box: unused.
    public float3 A;

    // Capsule: second end.
    public float3 B;

    // Sphere/capsule radius, already scaled by the collider's global scale.
    public float Radius;

    // Box only.
    public float4x4 WorldToLocal;
    public float4x4 LocalToWorld;
    public float3 Offset;
    public float3 HalfSize;

    // Box only: 1 / |global scale| per axis, so a particle radius in metres becomes local units.
    public float3 InvScale;

    // World AABB of the shape itself. Callers grow it by their own reach before testing.
    public float3 BoundsMin;
    public float3 BoundsMax;

    // The user this shape belongs to, for the player-collider own-body filter. Null for static shapes.
    public User? Owner;

    public static DynamicBoneColliderShape FromSphere(in float3 centre, float radius, User? owner = null)
    {
        var shape = new DynamicBoneColliderShape
        {
            Kind = ShapeKind.Sphere,
            A = centre,
            B = centre,
            Radius = radius,
            Owner = owner,
        };
        var pad = new float3(radius, radius, radius);
        shape.BoundsMin = centre - pad;
        shape.BoundsMax = centre + pad;
        return shape;
    }

    public static DynamicBoneColliderShape FromCapsule(in float3 a, in float3 b, float radius, User? owner = null)
    {
        var shape = new DynamicBoneColliderShape
        {
            Kind = ShapeKind.Capsule,
            A = a,
            B = b,
            Radius = radius,
            Owner = owner,
        };
        shape.BoundsMin = new float3(
            MathF.Min(a.x, b.x) - radius,
            MathF.Min(a.y, b.y) - radius,
            MathF.Min(a.z, b.z) - radius);
        shape.BoundsMax = new float3(
            MathF.Max(a.x, b.x) + radius,
            MathF.Max(a.y, b.y) + radius,
            MathF.Max(a.z, b.z) + radius);
        return shape;
    }

    public static DynamicBoneColliderShape FromBox(in float4x4 localToWorld, in float4x4 worldToLocal,
        in float3 offset, in float3 halfSize, in float3 invScale)
    {
        var shape = new DynamicBoneColliderShape
        {
            Kind = ShapeKind.Box,
            LocalToWorld = localToWorld,
            WorldToLocal = worldToLocal,
            Offset = offset,
            HalfSize = halfSize,
            InvScale = invScale,
        };

        // Eight corners through the world matrix: an oriented box's AABB is not its local extents.
        var min = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new float3(float.MinValue, float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            var corner = new float3(
                offset.x + ((c & 1) == 0 ? -halfSize.x : halfSize.x),
                offset.y + ((c & 2) == 0 ? -halfSize.y : halfSize.y),
                offset.z + ((c & 4) == 0 ? -halfSize.z : halfSize.z));
            var world = localToWorld.MultiplyPoint(in corner);
            min.x = MathF.Min(min.x, world.x); min.y = MathF.Min(min.y, world.y); min.z = MathF.Min(min.z, world.z);
            max.x = MathF.Max(max.x, world.x); max.y = MathF.Max(max.y, world.y); max.z = MathF.Max(max.z, world.z);
        }
        shape.BoundsMin = min;
        shape.BoundsMax = max;
        return shape;
    }

    // True when this shape's world AABB, grown by `margin`, touches the given box.
    public bool BoundsOverlap(in float3 min, in float3 max, float margin)
    {
        return BoundsMax.x + margin >= min.x && BoundsMin.x - margin <= max.x
            && BoundsMax.y + margin >= min.y && BoundsMin.y - margin <= max.y
            && BoundsMax.z + margin >= min.z && BoundsMin.z - margin <= max.z;
    }

    // Push the particle out of this shape. True when a correction was applied. Same maths the
    // collider components ran per particle before the snapshot existed.
    public bool Resolve(ref float3 position, float particleRadius)
    {
        switch (Kind)
        {
            case ShapeKind.Sphere:
                return ResolveSphere(ref position, particleRadius);
            case ShapeKind.Capsule:
                return ResolveCapsule(ref position, particleRadius);
            default:
                return ResolveBox(ref position, particleRadius);
        }
    }

    private bool ResolveSphere(ref float3 position, float particleRadius)
    {
        float minimum = Radius + particleRadius;
        float3 delta = position - A;
        float distanceSquared = delta.LengthSquared;
        if (distanceSquared >= minimum * minimum)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        float3 direction = distance > 1e-6f ? delta / distance : float3.Up;
        position = A + direction * minimum;
        return true;
    }

    private bool ResolveCapsule(ref float3 position, float particleRadius)
    {
        float3 closest = A;
        float3 ab = B - A;
        float lengthSquared = ab.LengthSquared;
        if (lengthSquared > 1e-8f)
        {
            float t = System.Math.Clamp(float3.Dot(position - A, ab) / lengthSquared, 0f, 1f);
            closest = A + ab * t;
        }

        float minimum = Radius + particleRadius;
        float3 delta = position - closest;
        float distanceSquared = delta.LengthSquared;
        if (distanceSquared >= minimum * minimum)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        float3 direction = distance > 1e-6f ? delta / distance : float3.Up;
        position = closest + direction * minimum;
        return true;
    }

    private bool ResolveBox(ref float3 position, float particleRadius)
    {
        float3 local = WorldToLocal.MultiplyPoint(in position) - Offset;
        float3 half = new float3(
            HalfSize.x + particleRadius * InvScale.x,
            HalfSize.y + particleRadius * InvScale.y,
            HalfSize.z + particleRadius * InvScale.z);

        if (MathF.Abs(local.x) >= half.x || MathF.Abs(local.y) >= half.y || MathF.Abs(local.z) >= half.z)
            return false;

        float px = half.x - MathF.Abs(local.x);
        float py = half.y - MathF.Abs(local.y);
        float pz = half.z - MathF.Abs(local.z);
        if (px <= py && px <= pz)
            local.x = MathF.CopySign(half.x, local.x);
        else if (py <= pz)
            local.y = MathF.CopySign(half.y, local.y);
        else
            local.z = MathF.CopySign(half.z, local.z);

        float3 back = local + Offset;
        position = LocalToWorld.MultiplyPoint(in back);
        return true;
    }
}
