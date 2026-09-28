// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Two flags, one per axis: what a per-component compare of two float2s gives back.
[StructLayout(LayoutKind.Sequential)]
public struct bool2 : IEquatable<bool2>
{
    public bool x;
    public bool y;

    public bool2(bool x, bool y)
    {
        this.x = x;
        this.y = y;
    }

    public bool2(bool value)
    {
        x = y = value;
    }

    public bool All => x && y;
    public bool Any => x || y;

    public static bool2 operator &(bool2 a, bool2 b) => new(a.x & b.x, a.y & b.y);
    public static bool2 operator |(bool2 a, bool2 b) => new(a.x | b.x, a.y | b.y);
    public static bool2 operator ^(bool2 a, bool2 b) => new(a.x ^ b.x, a.y ^ b.y);
    public static bool2 operator !(bool2 a) => new(!a.x, !a.y);

    public static bool operator ==(bool2 a, bool2 b) => a.x == b.x && a.y == b.y;
    public static bool operator !=(bool2 a, bool2 b) => !(a == b);

    public override bool Equals(object? obj) => obj is bool2 other && Equals(other);
    public bool Equals(bool2 other) => x == other.x && y == other.y;
    public override int GetHashCode() => HashCode.Combine(x, y);
    public override string ToString() => $"bool2({x}, {y})";
}
