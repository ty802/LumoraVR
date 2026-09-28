// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// A 2x2 matrix, column-major like float4x4: c0 and c1 are its columns.
[StructLayout(LayoutKind.Sequential)]
public struct float2x2 : IEquatable<float2x2>
{
    public float2 c0;
    public float2 c1;

    public static readonly float2x2 Identity = new(new float2(1f, 0f), new float2(0f, 1f));
    public static readonly float2x2 Zero = new(float2.Zero, float2.Zero);

    public float2x2(float2 c0, float2 c1)
    {
        this.c0 = c0;
        this.c1 = c1;
    }

    // Row by row, the way it is written down.
    public float2x2(float m00, float m01, float m10, float m11)
    {
        c0 = new float2(m00, m10);
        c1 = new float2(m01, m11);
    }

    public float this[int row, int column]
    {
        get
        {
            var c = column switch
            {
                0 => c0,
                1 => c1,
                _ => throw new IndexOutOfRangeException($"Invalid float2x2 column: {column}")
            };
            return row switch
            {
                0 => c.x,
                1 => c.y,
                _ => throw new IndexOutOfRangeException($"Invalid float2x2 row: {row}")
            };
        }
    }

    public float2 Row0 => new(c0.x, c1.x);
    public float2 Row1 => new(c0.y, c1.y);

    public float2x2 Transposed => new(Row0, Row1);

    public float Determinant => c0.x * c1.y - c1.x * c0.y;

    // A singular matrix inverts to zero rather than to infinities.
    public float2x2 Inverse
    {
        get
        {
            float det = Determinant;
            if (MathF.Abs(det) < 1e-12f)
                return Zero;
            float inv = 1f / det;
            return new float2x2(c1.y * inv, -c1.x * inv, -c0.y * inv, c0.x * inv);
        }
    }

    public static float2x2 Rotation(float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new float2x2(c, -s, s, c);
    }

    public static float2x2 Scale(float2 scale) => new(new float2(scale.x, 0f), new float2(0f, scale.y));

    // An angle and a scale such that Rotation(radians) * Scale(scale) gives the matrix back when it has no
    // shear. Scale is each column's length, x taken negative when the matrix mirrors; the angle is read
    // from x, or from y when x has no length.
    public void Decompose(out float radians, out float2 scale)
    {
        scale = new float2(c0.Length, c1.Length);
        var x = c0;
        if (Determinant < 0f)
        {
            scale.x = -scale.x;
            x = -x;
        }
        if (x.LengthSquared > 1e-20f)
            radians = MathF.Atan2(x.y, x.x);
        else if (c1.LengthSquared > 1e-20f)
            radians = MathF.Atan2(-c1.x, c1.y);
        else
            radians = 0f;
    }

    public static float2x2 operator *(float2x2 a, float2x2 b) => new(a * b.c0, a * b.c1);
    public static float2 operator *(float2x2 m, float2 v) => m.c0 * v.x + m.c1 * v.y;
    public static float2x2 operator *(float2x2 m, float s) => new(m.c0 * s, m.c1 * s);
    public static float2x2 operator +(float2x2 a, float2x2 b) => new(a.c0 + b.c0, a.c1 + b.c1);
    public static float2x2 operator -(float2x2 a, float2x2 b) => new(a.c0 - b.c0, a.c1 - b.c1);

    public static bool operator ==(float2x2 a, float2x2 b) => a.Equals(b);
    public static bool operator !=(float2x2 a, float2x2 b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is float2x2 other && Equals(other);
    public bool Equals(float2x2 other) => c0 == other.c0 && c1 == other.c1;
    public override int GetHashCode() => HashCode.Combine(c0, c1);
    public override string ToString() => $"float2x2({Row0}, {Row1})";
}
