// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Spherical harmonics through band 3: 16 RGB coefficients, the usual form for light arriving from every
// direction at a point.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL3 : IEquatable<SphericalHarmonicsL3>
{
    public const int Count = 16;

    public float3 sh0;
    public float3 sh1;
    public float3 sh2;
    public float3 sh3;
    public float3 sh4;
    public float3 sh5;
    public float3 sh6;
    public float3 sh7;
    public float3 sh8;
    public float3 sh9;
    public float3 sh10;
    public float3 sh11;
    public float3 sh12;
    public float3 sh13;
    public float3 sh14;
    public float3 sh15;

    public SphericalHarmonicsL3(float3 sh0, float3 sh1, float3 sh2, float3 sh3, float3 sh4, float3 sh5, float3 sh6, float3 sh7, float3 sh8, float3 sh9, float3 sh10, float3 sh11, float3 sh12, float3 sh13, float3 sh14, float3 sh15)
    {
        this.sh0 = sh0;
        this.sh1 = sh1;
        this.sh2 = sh2;
        this.sh3 = sh3;
        this.sh4 = sh4;
        this.sh5 = sh5;
        this.sh6 = sh6;
        this.sh7 = sh7;
        this.sh8 = sh8;
        this.sh9 = sh9;
        this.sh10 = sh10;
        this.sh11 = sh11;
        this.sh12 = sh12;
        this.sh13 = sh13;
        this.sh14 = sh14;
        this.sh15 = sh15;
    }

    public float3 this[int index]
    {
        get => index switch
        {
            0 => sh0,
            1 => sh1,
            2 => sh2,
            3 => sh3,
            4 => sh4,
            5 => sh5,
            6 => sh6,
            7 => sh7,
            8 => sh8,
            9 => sh9,
            10 => sh10,
            11 => sh11,
            12 => sh12,
            13 => sh13,
            14 => sh14,
            15 => sh15,
            _ => throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL3 index: {index}")
        };
        set
        {
            switch (index)
            {
                case 0: sh0 = value; break;
                case 1: sh1 = value; break;
                case 2: sh2 = value; break;
                case 3: sh3 = value; break;
                case 4: sh4 = value; break;
                case 5: sh5 = value; break;
                case 6: sh6 = value; break;
                case 7: sh7 = value; break;
                case 8: sh8 = value; break;
                case 9: sh9 = value; break;
                case 10: sh10 = value; break;
                case 11: sh11 = value; break;
                case 12: sh12 = value; break;
                case 13: sh13 = value; break;
                case 14: sh14 = value; break;
                case 15: sh15 = value; break;
                default: throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL3 index: {index}");
            }
        }
    }

    public float3 Evaluate(float3 direction)
    {
        Span<float3> coefficients = stackalloc float3[Count];
        for (int i = 0; i < Count; i++)
            coefficients[i] = this[i];
        return SphericalHarmonics.Evaluate(coefficients, direction);
    }

    public static SphericalHarmonicsL3 operator +(SphericalHarmonicsL3 a, SphericalHarmonicsL3 b) => new(a.sh0 + b.sh0, a.sh1 + b.sh1, a.sh2 + b.sh2, a.sh3 + b.sh3, a.sh4 + b.sh4, a.sh5 + b.sh5, a.sh6 + b.sh6, a.sh7 + b.sh7, a.sh8 + b.sh8, a.sh9 + b.sh9, a.sh10 + b.sh10, a.sh11 + b.sh11, a.sh12 + b.sh12, a.sh13 + b.sh13, a.sh14 + b.sh14, a.sh15 + b.sh15);
    public static SphericalHarmonicsL3 operator *(SphericalHarmonicsL3 a, float s) => new(a.sh0 * s, a.sh1 * s, a.sh2 * s, a.sh3 * s, a.sh4 * s, a.sh5 * s, a.sh6 * s, a.sh7 * s, a.sh8 * s, a.sh9 * s, a.sh10 * s, a.sh11 * s, a.sh12 * s, a.sh13 * s, a.sh14 * s, a.sh15 * s);

    public static bool operator ==(SphericalHarmonicsL3 a, SphericalHarmonicsL3 b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL3 a, SphericalHarmonicsL3 b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL3 other && Equals(other);
    public bool Equals(SphericalHarmonicsL3 other) => sh0 == other.sh0 && sh1 == other.sh1 && sh2 == other.sh2 && sh3 == other.sh3 && sh4 == other.sh4 && sh5 == other.sh5 && sh6 == other.sh6 && sh7 == other.sh7 && sh8 == other.sh8 && sh9 == other.sh9 && sh10 == other.sh10 && sh11 == other.sh11 && sh12 == other.sh12 && sh13 == other.sh13 && sh14 == other.sh14 && sh15 == other.sh15;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(sh0);
        hash.Add(sh1);
        hash.Add(sh2);
        hash.Add(sh3);
        hash.Add(sh4);
        hash.Add(sh5);
        hash.Add(sh6);
        hash.Add(sh7);
        hash.Add(sh8);
        hash.Add(sh9);
        hash.Add(sh10);
        hash.Add(sh11);
        hash.Add(sh12);
        hash.Add(sh13);
        hash.Add(sh14);
        hash.Add(sh15);
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL3";
}

// The same 16 coefficients of any kind, one number or one color each: harmonics of a single value, or of
// a color with its alpha.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL3<T> : IEquatable<SphericalHarmonicsL3<T>> where T : unmanaged, IEquatable<T>
{
    public const int Count = 16;

    public T sh0;
    public T sh1;
    public T sh2;
    public T sh3;
    public T sh4;
    public T sh5;
    public T sh6;
    public T sh7;
    public T sh8;
    public T sh9;
    public T sh10;
    public T sh11;
    public T sh12;
    public T sh13;
    public T sh14;
    public T sh15;

    public SphericalHarmonicsL3(T sh0, T sh1, T sh2, T sh3, T sh4, T sh5, T sh6, T sh7, T sh8, T sh9, T sh10, T sh11,
        T sh12, T sh13, T sh14, T sh15)
    {
        this.sh0 = sh0;
        this.sh1 = sh1;
        this.sh2 = sh2;
        this.sh3 = sh3;
        this.sh4 = sh4;
        this.sh5 = sh5;
        this.sh6 = sh6;
        this.sh7 = sh7;
        this.sh8 = sh8;
        this.sh9 = sh9;
        this.sh10 = sh10;
        this.sh11 = sh11;
        this.sh12 = sh12;
        this.sh13 = sh13;
        this.sh14 = sh14;
        this.sh15 = sh15;
    }

    // Every field is a T, laid out one after another, so the coefficients read as a run of them.
    public T this[int index]
    {
        get => MemoryMarshal.CreateReadOnlySpan(ref sh0, Count)[index];
        set => MemoryMarshal.CreateSpan(ref sh0, Count)[index] = value;
    }

    public static bool operator ==(SphericalHarmonicsL3<T> a, SphericalHarmonicsL3<T> b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL3<T> a, SphericalHarmonicsL3<T> b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL3<T> other && Equals(other);

    public bool Equals(SphericalHarmonicsL3<T> other)
    {
        var comparer = EqualityComparer<T>.Default;
        return comparer.Equals(sh0, other.sh0) && comparer.Equals(sh1, other.sh1) && comparer.Equals(sh2, other.sh2) &&
            comparer.Equals(sh3, other.sh3) && comparer.Equals(sh4, other.sh4) && comparer.Equals(sh5, other.sh5) &&
            comparer.Equals(sh6, other.sh6) && comparer.Equals(sh7, other.sh7) && comparer.Equals(sh8, other.sh8) &&
            comparer.Equals(sh9, other.sh9) && comparer.Equals(sh10, other.sh10) && comparer.Equals(sh11, other.sh11) &&
            comparer.Equals(sh12, other.sh12) && comparer.Equals(sh13, other.sh13) &&
            comparer.Equals(sh14, other.sh14) && comparer.Equals(sh15, other.sh15);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(sh0);
        hash.Add(sh1);
        hash.Add(sh2);
        hash.Add(sh3);
        hash.Add(sh4);
        hash.Add(sh5);
        hash.Add(sh6);
        hash.Add(sh7);
        hash.Add(sh8);
        hash.Add(sh9);
        hash.Add(sh10);
        hash.Add(sh11);
        hash.Add(sh12);
        hash.Add(sh13);
        hash.Add(sh14);
        hash.Add(sh15);
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL3<" + typeof(T).Name + ">";
}
