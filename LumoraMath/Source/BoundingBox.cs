// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Math;

/// <summary>
/// Axis-aligned bounding box (AABB).
/// Stores minimum and maximum points.
/// </summary>
public struct BoundingBox : System.IEquatable<BoundingBox>
{
    /// <summary>Minimum point of the bounding box</summary>
    public float3 Min;

    /// <summary>Maximum point of the bounding box</summary>
    public float3 Max;

    // Constructors

    public BoundingBox(float3 min, float3 max)
    {
        Min = min;
        Max = max;
    }

    // Properties

    /// <summary>Center of the bounding box</summary>
    public float3 Center => (Min + Max) * 0.5f;

    /// <summary>Size of the bounding box</summary>
    public float3 Size => Max - Min;

    /// <summary>Half extents of the bounding box</summary>
    public float3 Extents => Size * 0.5f;

    // Holds nothing: the box MakeEmpty leaves, which the first point or box encapsulated fills exactly.
    public static BoundingBox Empty => new(
        new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
        new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity));

    // Min past max on some axis, so no point is inside.
    public bool IsEmpty => Min.x > Max.x || Min.y > Max.y || Min.z > Max.z;

    // The box around this one carried through an affine matrix: the centre moved as a point, the extents
    // by the absolute rotation and scale. The bottom row is not read. An empty box stays empty.
    public BoundingBox Transformed(in float4x4 matrix)
    {
        if (IsEmpty)
            return Empty;
        var center = Center;
        var extents = Extents;
        var moved = new float3(
            matrix.c0.x * center.x + matrix.c1.x * center.y + matrix.c2.x * center.z + matrix.c3.x,
            matrix.c0.y * center.x + matrix.c1.y * center.y + matrix.c2.y * center.z + matrix.c3.y,
            matrix.c0.z * center.x + matrix.c1.z * center.y + matrix.c2.z * center.z + matrix.c3.z);
        var reach = new float3(
            System.Math.Abs(matrix.c0.x) * extents.x + System.Math.Abs(matrix.c1.x) * extents.y + System.Math.Abs(matrix.c2.x) * extents.z,
            System.Math.Abs(matrix.c0.y) * extents.x + System.Math.Abs(matrix.c1.y) * extents.y + System.Math.Abs(matrix.c2.y) * extents.z,
            System.Math.Abs(matrix.c0.z) * extents.x + System.Math.Abs(matrix.c1.z) * extents.y + System.Math.Abs(matrix.c2.z) * extents.z);
        return new BoundingBox(moved - reach, moved + reach);
    }

    // Methods

    /// <summary>
    /// Make the bounding box empty (inverted min/max).
    /// </summary>
    public void MakeEmpty()
    {
        Min = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Max = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    }

    /// <summary>
    /// Expand the bounding box to include a point.
    /// </summary>
    public void Encapsulate(float3 point)
    {
        Min = new float3(
            System.Math.Min(Min.x, point.x),
            System.Math.Min(Min.y, point.y),
            System.Math.Min(Min.z, point.z)
        );
        Max = new float3(
            System.Math.Max(Max.x, point.x),
            System.Math.Max(Max.y, point.y),
            System.Math.Max(Max.z, point.z)
        );
    }

    /// <summary>
    /// Expand the bounding box to include another bounding box.
    /// </summary>
    public void Encapsulate(BoundingBox other)
    {
        Min = new float3(
            System.Math.Min(Min.x, other.Min.x),
            System.Math.Min(Min.y, other.Min.y),
            System.Math.Min(Min.z, other.Min.z)
        );
        Max = new float3(
            System.Math.Max(Max.x, other.Max.x),
            System.Math.Max(Max.y, other.Max.y),
            System.Math.Max(Max.z, other.Max.z)
        );
    }

    /// <summary>
    /// Check if a point is inside the bounding box.
    /// </summary>
    public bool Contains(float3 point)
    {
        return point.x >= Min.x && point.x <= Max.x &&
               point.y >= Min.y && point.y <= Max.y &&
               point.z >= Min.z && point.z <= Max.z;
    }

    /// <summary>
    /// Check if this bounding box intersects another.
    /// </summary>
    public bool Intersects(BoundingBox other)
    {
        return !(Max.x < other.Min.x || Min.x > other.Max.x ||
                 Max.y < other.Min.y || Min.y > other.Max.y ||
                 Max.z < other.Min.z || Min.z > other.Max.z);
    }

    // Without it every write of a box field compared the two through reflection, boxing both: a tenth of the
    // time it took to copy a slot holding one.
    public bool Equals(BoundingBox other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    public override bool Equals(object? obj) => obj is BoundingBox other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(Min, Max);
}
