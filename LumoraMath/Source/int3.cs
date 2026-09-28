// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Three ints: voxel cells, grid positions, counts along three axes.
[StructLayout(LayoutKind.Sequential)]
public struct int3 : IEquatable<int3>
{
    public int x;
    public int y;
    public int z;

    public int3(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public int3(int value)
    {
        x = y = z = value;
    }

    public static int3 Zero => new(0, 0, 0);
    public static int3 One => new(1, 1, 1);

    public int this[int index]
    {
        get => index switch
        {
            0 => x,
            1 => y,
            2 => z,
            _ => throw new IndexOutOfRangeException($"Invalid int3 index: {index}")
        };
        set
        {
            switch (index)
            {
                case 0: x = value; break;
                case 1: y = value; break;
                case 2: z = value; break;
                default: throw new IndexOutOfRangeException($"Invalid int3 index: {index}");
            }
        }
    }

    public static int3 operator +(int3 a, int3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static int3 operator -(int3 a, int3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static int3 operator *(int3 a, int3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
    public static int3 operator *(int3 a, int b) => new(a.x * b, a.y * b, a.z * b);
    public static int3 operator *(int a, int3 b) => new(a * b.x, a * b.y, a * b.z);
    public static int3 operator /(int3 a, int b) => new(a.x / b, a.y / b, a.z / b);
    public static int3 operator -(int3 a) => new(-a.x, -a.y, -a.z);

    public static bool operator ==(int3 a, int3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    public static bool operator !=(int3 a, int3 b) => !(a == b);

    public override bool Equals(object? obj) => obj is int3 other && Equals(other);
    public bool Equals(int3 other) => x == other.x && y == other.y && z == other.z;
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    public override string ToString() => $"int3({x}, {y}, {z})";
}
