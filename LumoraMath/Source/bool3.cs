// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Three flags, one per axis: what a per-component compare of two float3s gives back.
[StructLayout(LayoutKind.Sequential)]
public struct bool3 : IEquatable<bool3>
{
    public bool x;
    public bool y;
    public bool z;

    public bool3(bool x, bool y, bool z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public bool3(bool value)
    {
        x = y = z = value;
    }

    public bool All => x && y && z;
    public bool Any => x || y || z;

    public static bool3 operator &(bool3 a, bool3 b) => new(a.x & b.x, a.y & b.y, a.z & b.z);
    public static bool3 operator |(bool3 a, bool3 b) => new(a.x | b.x, a.y | b.y, a.z | b.z);
    public static bool3 operator ^(bool3 a, bool3 b) => new(a.x ^ b.x, a.y ^ b.y, a.z ^ b.z);
    public static bool3 operator !(bool3 a) => new(!a.x, !a.y, !a.z);

    public static bool operator ==(bool3 a, bool3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    public static bool operator !=(bool3 a, bool3 b) => !(a == b);

    public override bool Equals(object? obj) => obj is bool3 other && Equals(other);
    public bool Equals(bool3 other) => x == other.x && y == other.y && z == other.z;
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    public override string ToString() => $"bool3({x}, {y}, {z})";
}
