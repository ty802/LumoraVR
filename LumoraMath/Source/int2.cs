// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// A pair of ints: grid cells, pixel coordinates, sizes in whole units.
[StructLayout(LayoutKind.Sequential)]
public struct int2 : IEquatable<int2>
{
    public int x;
    public int y;

    public int2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public int2(int value)
    {
        x = y = value;
    }

    public static int2 Zero => new(0, 0);
    public static int2 One => new(1, 1);

    public int this[int index]
    {
        get => index switch
        {
            0 => x,
            1 => y,
            _ => throw new IndexOutOfRangeException($"Invalid int2 index: {index}")
        };
        set
        {
            switch (index)
            {
                case 0: x = value; break;
                case 1: y = value; break;
                default: throw new IndexOutOfRangeException($"Invalid int2 index: {index}");
            }
        }
    }

    public static int2 operator +(int2 a, int2 b) => new(a.x + b.x, a.y + b.y);
    public static int2 operator -(int2 a, int2 b) => new(a.x - b.x, a.y - b.y);
    public static int2 operator *(int2 a, int2 b) => new(a.x * b.x, a.y * b.y);
    public static int2 operator *(int2 a, int b) => new(a.x * b, a.y * b);
    public static int2 operator *(int a, int2 b) => new(a * b.x, a * b.y);
    public static int2 operator /(int2 a, int b) => new(a.x / b, a.y / b);
    public static int2 operator -(int2 a) => new(-a.x, -a.y);

    public static bool operator ==(int2 a, int2 b) => a.x == b.x && a.y == b.y;
    public static bool operator !=(int2 a, int2 b) => !(a == b);

    public override bool Equals(object? obj) => obj is int2 other && Equals(other);
    public bool Equals(int2 other) => x == other.x && y == other.y;
    public override int GetHashCode() => HashCode.Combine(x, y);
    public override string ToString() => $"int2({x}, {y})";
}
