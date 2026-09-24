using System;
using Godot;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Godot.Helpers;

// Physics bodies stay at unit scale. Bake object scale into the shape and its local offset.
internal static class ColliderShapeScaling
{
    public static Vector3 ScaleOf(Collider collider)
    {
        var scale = collider.Slot.GlobalScale;
        return new Vector3(scale.x, scale.y, scale.z);
    }

    public static Vector3 OffsetOf(Collider collider, Vector3 scale)
    {
        var offset = collider.Offset.Value;
        return new Vector3(offset.x, offset.y, offset.z) * scale;
    }

    public static Shape3D? Update(Collider collider, Vector3 scale, Shape3D? previous, out bool changed)
    {
        changed = false;
        var absolute = scale.Abs();
        switch (collider)
        {
            case BoxCollider box:
                var size = box.Size.Value;
                var dimensions = (new Vector3(size.x, size.y, size.z) * absolute).Abs().Max(Vector3.One * .0001f);
                var boxShape = previous as BoxShape3D ?? new BoxShape3D();
                changed = previous != boxShape || boxShape.Size != dimensions;
                if (changed) boxShape.Size = dimensions;
                return boxShape;
            case SphereCollider sphere:
                float sphereRadius = MathF.Max(.0001f, MathF.Abs(sphere.Radius.Value) * MathF.Max(absolute.X, MathF.Max(absolute.Y, absolute.Z)));
                var sphereShape = previous as SphereShape3D ?? new SphereShape3D();
                changed = previous != sphereShape || sphereShape.Radius != sphereRadius;
                if (changed) sphereShape.Radius = sphereRadius;
                return sphereShape;
            case CapsuleCollider capsule:
                float radius = MathF.Max(.0001f, MathF.Abs(capsule.Radius.Value) * MathF.Max(absolute.X, absolute.Z));
                float height = MathF.Max(2f * radius, MathF.Abs(capsule.Height.Value) * absolute.Y);
                var capsuleShape = previous as CapsuleShape3D ?? new CapsuleShape3D();
                changed = previous != capsuleShape || capsuleShape.Radius != radius || capsuleShape.Height != height;
                if (changed)
                {
                    // Godot clamps radius against height; establish enough height before growing it.
                    capsuleShape.Height = MathF.Max(height, capsuleShape.Height);
                    capsuleShape.Radius = radius;
                    capsuleShape.Height = height;
                }
                return capsuleShape;
            case CylinderCollider cylinder:
                float cylinderRadius = MathF.Max(.0001f, MathF.Abs(cylinder.Radius.Value) * MathF.Max(absolute.X, absolute.Z));
                float cylinderHeight = MathF.Max(.0001f, MathF.Abs(cylinder.Height.Value) * absolute.Y);
                var cylinderShape = previous as CylinderShape3D ?? new CylinderShape3D();
                changed = previous != cylinderShape || cylinderShape.Radius != cylinderRadius || cylinderShape.Height != cylinderHeight;
                if (changed) { cylinderShape.Radius = cylinderRadius; cylinderShape.Height = cylinderHeight; }
                return cylinderShape;
            case ConeCollider cone:
                const int segments = 32;
                var points = new Vector3[segments + 1];
                for (int i = 0; i < segments; i++)
                {
                    float angle = MathF.Tau * i / segments;
                    points[i] = new Vector3(MathF.Cos(angle) * cone.Radius.Value, -cone.Height.Value * .5f,
                        MathF.Sin(angle) * cone.Radius.Value) * scale;
                }
                points[segments] = new Vector3(0, cone.Height.Value * .5f, 0) * scale;
                var coneShape = previous as ConvexPolygonShape3D ?? new ConvexPolygonShape3D();
                changed = previous != coneShape || !coneShape.Points.AsSpan().SequenceEqual(points);
                if (changed) coneShape.Points = points;
                return coneShape;
            case TriangleCollider triangle:
                Vector3 Point(float3 p) => new Vector3(p.x, p.y, p.z) * scale;
                var faces = new[] { Point(triangle.A.Value), Point(triangle.B.Value), Point(triangle.C.Value) };
                var triangleShape = previous as ConcavePolygonShape3D ?? new ConcavePolygonShape3D { BackfaceCollision = true };
                changed = previous != triangleShape || !triangleShape.Data.AsSpan().SequenceEqual(faces);
                if (changed) triangleShape.Data = faces;
                return triangleShape;
            default:
                return null;
        }
    }
}
