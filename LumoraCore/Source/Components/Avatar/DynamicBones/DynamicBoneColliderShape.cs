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
        Plane,
    }

    public ShapeKind Kind;

    // Sphere: centre. Capsule: first end. Plane: a point on it. Box: unused.
    public float3 A;

    // Capsule: second end. Plane: unit normal, the side particles stay on.
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

    // Player body capsule: soft bodies push against it, bone chains never see it.
    public bool SoftBodyOnly;

    // A player's head. The wearer's own chains never feel it: it is a generic sphere, and the avatar's own
    // colliders know the real skull.
    public bool Head;

    // Sphere and capsule: particles are kept INSIDE rather than pushed out, so hair or fluff stays within it.
    public bool Inside;

    // Sphere and capsule: the whole bone from its parent particle is kept out, not only the particle, so a
    // long bone cannot pass through a small collider between its two ends.
    public bool Segments;

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

    // Bounds are finite around the point: broadphase tests still pass for anything near it, and callers
    // that watch BoundsMin for movement see the plane move.
    public static DynamicBoneColliderShape FromPlane(in float3 point, in float3 normal)
    {
        const float reach = 10000f;
        var pad = new float3(reach, reach, reach);
        return new DynamicBoneColliderShape
        {
            Kind = ShapeKind.Plane,
            A = point,
            B = normal,
            BoundsMin = point - pad,
            BoundsMax = point + pad,
        };
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
        if (Inside && Kind is ShapeKind.Sphere or ShapeKind.Capsule)
            return ResolveInside(ref position, particleRadius);
        switch (Kind)
        {
            case ShapeKind.Sphere:
                return ResolveSphere(ref position, particleRadius);
            case ShapeKind.Capsule:
                return ResolveCapsule(ref position, particleRadius);
            case ShapeKind.Plane:
                return ResolvePlane(ref position, particleRadius);
            default:
                return ResolveBox(ref position, particleRadius);
        }
    }

    // Resolve for a particle hanging from `anchor`: the particle first, then the bone between them, turned about
    // the anchor just far enough that it clears the shape. A bone whose anchor is already inside is left alone;
    // the anchor's own test deals with that.
    public bool Resolve(ref float3 position, in float3 anchor, float particleRadius)
    {
        bool moved = Resolve(ref position, particleRadius);
        if (!Segments || Inside || Kind is not (ShapeKind.Sphere or ShapeKind.Capsule))
            return moved;
        return ResolveSegment(ref position, in anchor, particleRadius) | moved;
    }

    // For a shape that keeps bones inside: puts a particle that ended up outside back in without stretching its bone,
    // on the circle where the sphere of points one bone length from the anchor meets the inside of the shape, at the
    // place on it nearest where the particle was. Run after the bone's length is fixed, or that would push it out again.
    public bool KeepInside(ref float3 position, in float3 anchor, float length, float particleRadius)
    {
        if (!Inside || Kind is not (ShapeKind.Sphere or ShapeKind.Capsule))
            return false;
        float3 core = A;
        if (Kind == ShapeKind.Capsule)
        {
            float3 ab = B - A;
            float lengthSquared = ab.LengthSquared;
            if (lengthSquared > 1e-8f)
                core = A + ab * System.Math.Clamp(float3.Dot(position - A, ab) / lengthSquared, 0f, 1f);
        }
        float limit = MathF.Max(Radius - particleRadius, 0f);
        if ((position - core).LengthSquared <= limit * limit)
            return false;
        float3 toCore = core - anchor;
        float d = toCore.Length;
        if (d < 1e-6f || length < 1e-6f)
            return ResolveInside(ref position, particleRadius);
        float3 u = toCore / d;
        float along = (length * length - limit * limit + d * d) / (2f * d);
        float h2 = length * length - along * along;
        if (h2 < 0f)
        {
            // The bone cannot reach the inside at its length: point it at the middle.
            position = anchor + u * length;
            return true;
        }
        float3 side = position - anchor;
        side -= u * float3.Dot(side, u);
        if (side.LengthSquared < 1e-12f)
        {
            side = float3.Cross(u, float3.Up);
            if (side.LengthSquared < 1e-12f)
                side = float3.Cross(u, float3.Right);
        }
        position = anchor + u * along + side.Normalized * MathF.Sqrt(h2);
        return true;
    }

    private bool ResolveInside(ref float3 position, float particleRadius)
    {
        float3 core = A;
        if (Kind == ShapeKind.Capsule)
        {
            float3 ab = B - A;
            float lengthSquared = ab.LengthSquared;
            if (lengthSquared > 1e-8f)
                core = A + ab * System.Math.Clamp(float3.Dot(position - A, ab) / lengthSquared, 0f, 1f);
        }
        float limit = MathF.Max(Radius - particleRadius, 0f);
        float3 delta = position - core;
        float distanceSquared = delta.LengthSquared;
        if (distanceSquared <= limit * limit)
            return false;
        float distance = MathF.Sqrt(distanceSquared);
        position = core + delta / distance * limit;
        return true;
    }

    private bool ResolveSegment(ref float3 position, in float3 anchor, float particleRadius)
    {
        float3 bone = position - anchor;
        float length = bone.Length;
        if (length < 1e-6f)
            return false;
        float3 dir = bone / length;

        // The shape's centre nearest the bone: a sphere's own, a capsule's point on its core closest to the bone.
        float3 centre = A;
        if (Kind == ShapeKind.Capsule)
            centre = ClosestOnSegment(A, B, anchor, position);

        float minimum = Radius + particleRadius;
        float3 toCentre = centre - anchor;
        float reach = toCentre.Length;
        if (reach <= minimum || reach < 1e-6f)
            return false;
        float along = float3.Dot(toCentre, dir);
        // Past either end the nearest point of the bone is an end, which the particle tests cover.
        if (along <= 0f || along >= length)
            return false;
        float3 side = toCentre - dir * along;
        if (side.LengthSquared >= minimum * minimum)
            return false;

        // Turn the bone in the plane it shares with the centre until it passes the shape at the minimum distance.
        float3 axisToCentre = toCentre / reach;
        float clear = MathF.Asin(System.Math.Clamp(minimum / reach, 0f, 1f));
        float3 away = dir - axisToCentre * float3.Dot(dir, axisToCentre);
        if (away.LengthSquared < 1e-10f)
        {
            away = float3.Cross(axisToCentre, float3.Up);
            if (away.LengthSquared < 1e-10f)
                away = float3.Cross(axisToCentre, float3.Right);
        }
        away = away.Normalized;
        position = anchor + (axisToCentre * MathF.Cos(clear) + away * MathF.Sin(clear)) * length;
        return true;
    }

    // The point of segment ab nearest to segment pq.
    private static float3 ClosestOnSegment(in float3 a, in float3 b, in float3 p, in float3 q)
    {
        float3 d1 = b - a;
        float3 d2 = q - p;
        float3 r = a - p;
        float aa = float3.Dot(d1, d1);
        float ee = float3.Dot(d2, d2);
        if (aa < 1e-10f)
            return a;
        float f = float3.Dot(d2, r);
        float c = float3.Dot(d1, r);
        float s;
        if (ee < 1e-10f)
        {
            s = System.Math.Clamp(-c / aa, 0f, 1f);
        }
        else
        {
            float bb = float3.Dot(d1, d2);
            float denominator = aa * ee - bb * bb;
            s = denominator > 1e-10f ? System.Math.Clamp((bb * f - c * ee) / denominator, 0f, 1f) : 0f;
            float t = (bb * s + f) / ee;
            if (t < 0f)
                s = System.Math.Clamp(-c / aa, 0f, 1f);
            else if (t > 1f)
                s = System.Math.Clamp((bb - c) / aa, 0f, 1f);
        }
        return a + d1 * s;
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

    private bool ResolvePlane(ref float3 position, float particleRadius)
    {
        float depth = float3.Dot(position - A, B) - particleRadius;
        if (depth >= 0f)
            return false;
        position -= B * depth;
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
