// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// A 3x3 matrix, column-major like float4x4: a rotation and scale with no translation.
[StructLayout(LayoutKind.Sequential)]
public struct float3x3 : IEquatable<float3x3>
{
    public float3 c0;
    public float3 c1;
    public float3 c2;

    public static readonly float3x3 Identity = new(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f));
    public static readonly float3x3 Zero = new(float3.Zero, float3.Zero, float3.Zero);

    public float3x3(float3 c0, float3 c1, float3 c2)
    {
        this.c0 = c0;
        this.c1 = c1;
        this.c2 = c2;
    }

    public float this[int row, int column]
    {
        get
        {
            var c = column switch
            {
                0 => c0,
                1 => c1,
                2 => c2,
                _ => throw new IndexOutOfRangeException($"Invalid float3x3 column: {column}")
            };
            return row switch
            {
                0 => c.x,
                1 => c.y,
                2 => c.z,
                _ => throw new IndexOutOfRangeException($"Invalid float3x3 row: {row}")
            };
        }
    }

    public float3 Row0 => new(c0.x, c1.x, c2.x);
    public float3 Row1 => new(c0.y, c1.y, c2.y);
    public float3 Row2 => new(c0.z, c1.z, c2.z);

    public float3x3 Transposed => new(Row0, Row1, Row2);

    public float Determinant => float3.Dot(c0, float3.Cross(c1, c2));

    // A singular matrix inverts to zero rather than to infinities.
    public float3x3 Inverse
    {
        get
        {
            float det = Determinant;
            if (MathF.Abs(det) < 1e-12f)
                return Zero;
            float inv = 1f / det;
            var r0 = float3.Cross(c1, c2) * inv;
            var r1 = float3.Cross(c2, c0) * inv;
            var r2 = float3.Cross(c0, c1) * inv;
            return new float3x3(r0, r1, r2).Transposed;
        }
    }

    public static float3x3 FromRotation(floatQ q)
    {
        float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
        return new float3x3(
            new float3(1f - 2f * (yy + zz), 2f * (xy + wz), 2f * (xz - wy)),
            new float3(2f * (xy - wz), 1f - 2f * (xx + zz), 2f * (yz + wx)),
            new float3(2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (xx + yy)));
    }

    public static float3x3 Scale(float3 scale)
        => new(new float3(scale.x, 0f, 0f), new float3(0f, scale.y, 0f), new float3(0f, 0f, scale.z));

    // Rotation and scale such that FromRotation(rotation) * Scale(scale) gives the matrix back when it has
    // no shear. Scale is each column's length, x taken negative when the matrix mirrors.
    public void Decompose(out floatQ rotation, out float3 scale)
    {
        scale = new float3(c0.Length, c1.Length, c2.Length);
        if (Determinant < 0f)
            scale.x = -scale.x;
        rotation = ToRotation();
    }

    // The rotation part as a unit quaternion. The columns are squared up first, z kept and y made square
    // to it, so scale and shear drop out; a mirroring matrix is read with x flipped, the flip Decompose
    // hands the x scale. An axis of zero length is made from the ones that are left, and a zero matrix is
    // no rotation at all. -xlinka
    public floatQ ToRotation()
    {
        const float tiny = 1e-20f;
        var x = Determinant < 0f ? -c0 : c0;

        var forward = c2;
        if (forward.LengthSquared <= tiny)
            forward = float3.Cross(x, c1);
        if (forward.LengthSquared <= tiny)
        {
            var axis = (x.LengthSquared > tiny ? x : c1).Normalized;
            forward = float3.Forward - axis * float3.Dot(float3.Forward, axis);
            if (forward.LengthSquared <= tiny)
                forward = float3.Right - axis * float3.Dot(float3.Right, axis);
        }
        forward = forward.Normalized;

        var up = c1 - forward * float3.Dot(c1, forward);
        if (up.LengthSquared <= tiny)
            up = float3.Cross(forward, x);
        if (up.LengthSquared <= tiny)
            up = float3.Up - forward * float3.Dot(float3.Up, forward);
        if (up.LengthSquared <= tiny)
            up = float3.Backward - forward * float3.Dot(float3.Backward, forward);
        up = up.Normalized;
        var right = float3.Cross(up, forward);

        // Read off the largest of w, x, y and z first, so nothing is divided by a number near zero.
        float m00 = right.x, m11 = up.y, m22 = forward.z;
        float trace = m00 + m11 + m22;
        floatQ q;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            q = new floatQ((up.z - forward.y) / s, (forward.x - right.z) / s, (right.y - up.x) / s, 0.25f * s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            q = new floatQ(0.25f * s, (up.x + right.y) / s, (forward.x + right.z) / s, (up.z - forward.y) / s);
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            q = new floatQ((up.x + right.y) / s, 0.25f * s, (forward.y + up.z) / s, (forward.x - right.z) / s);
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            q = new floatQ((forward.x + right.z) / s, (forward.y + up.z) / s, 0.25f * s, (right.y - up.x) / s);
        }
        return q.Normalized;
    }

    public static float3x3 operator *(float3x3 a, float3x3 b) => new(a * b.c0, a * b.c1, a * b.c2);
    public static float3 operator *(float3x3 m, float3 v) => m.c0 * v.x + m.c1 * v.y + m.c2 * v.z;
    public static float3x3 operator *(float3x3 m, float s) => new(m.c0 * s, m.c1 * s, m.c2 * s);
    public static float3x3 operator +(float3x3 a, float3x3 b) => new(a.c0 + b.c0, a.c1 + b.c1, a.c2 + b.c2);
    public static float3x3 operator -(float3x3 a, float3x3 b) => new(a.c0 - b.c0, a.c1 - b.c1, a.c2 - b.c2);

    public static bool operator ==(float3x3 a, float3x3 b) => a.Equals(b);
    public static bool operator !=(float3x3 a, float3x3 b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is float3x3 other && Equals(other);
    public bool Equals(float3x3 other) => c0 == other.c0 && c1 == other.c1 && c2 == other.c2;
    public override int GetHashCode() => HashCode.Combine(c0, c1, c2);
    public override string ToString() => $"float3x3({Row0}, {Row1}, {Row2})";
}
