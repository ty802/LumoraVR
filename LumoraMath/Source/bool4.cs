// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Four flags, one per component: what a per-component compare of two float4s or colors gives back.
[StructLayout(LayoutKind.Sequential)]
public struct bool4 : IEquatable<bool4>
{
    public bool x;
    public bool y;
    public bool z;
    public bool w;

    public bool4(bool x, bool y, bool z, bool w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public bool4(bool value)
    {
        x = y = z = w = value;
    }

    public bool All => x && y && z && w;
    public bool Any => x || y || z || w;

    public static bool4 operator &(bool4 a, bool4 b) => new(a.x & b.x, a.y & b.y, a.z & b.z, a.w & b.w);
    public static bool4 operator |(bool4 a, bool4 b) => new(a.x | b.x, a.y | b.y, a.z | b.z, a.w | b.w);
    public static bool4 operator ^(bool4 a, bool4 b) => new(a.x ^ b.x, a.y ^ b.y, a.z ^ b.z, a.w ^ b.w);
    public static bool4 operator !(bool4 a) => new(!a.x, !a.y, !a.z, !a.w);

    public static bool operator ==(bool4 a, bool4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    public static bool operator !=(bool4 a, bool4 b) => !(a == b);

    public override bool Equals(object? obj) => obj is bool4 other && Equals(other);
    public bool Equals(bool4 other) => x == other.x && y == other.y && z == other.z && w == other.w;
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    public override string ToString() => $"bool4({x}, {y}, {z}, {w})";
}
