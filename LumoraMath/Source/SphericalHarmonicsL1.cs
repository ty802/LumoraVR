// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Spherical harmonics through band 1: 4 RGB coefficients, the usual form for light arriving from every
// direction at a point.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL1 : IEquatable<SphericalHarmonicsL1>
{
    public const int Count = 4;

    public float3 sh0;
    public float3 sh1;
    public float3 sh2;
    public float3 sh3;

    public SphericalHarmonicsL1(float3 sh0, float3 sh1, float3 sh2, float3 sh3)
    {
        this.sh0 = sh0;
        this.sh1 = sh1;
        this.sh2 = sh2;
        this.sh3 = sh3;
    }

    public float3 this[int index]
    {
        get => index switch
        {
            0 => sh0,
            1 => sh1,
            2 => sh2,
            3 => sh3,
            _ => throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL1 index: {index}")
        };
        set
        {
            switch (index)
            {
                case 0: sh0 = value; break;
                case 1: sh1 = value; break;
                case 2: sh2 = value; break;
                case 3: sh3 = value; break;
                default: throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL1 index: {index}");
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

    public static SphericalHarmonicsL1 operator +(SphericalHarmonicsL1 a, SphericalHarmonicsL1 b) => new(a.sh0 + b.sh0, a.sh1 + b.sh1, a.sh2 + b.sh2, a.sh3 + b.sh3);
    public static SphericalHarmonicsL1 operator *(SphericalHarmonicsL1 a, float s) => new(a.sh0 * s, a.sh1 * s, a.sh2 * s, a.sh3 * s);

    public static bool operator ==(SphericalHarmonicsL1 a, SphericalHarmonicsL1 b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL1 a, SphericalHarmonicsL1 b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL1 other && Equals(other);
    public bool Equals(SphericalHarmonicsL1 other) => sh0 == other.sh0 && sh1 == other.sh1 && sh2 == other.sh2 && sh3 == other.sh3;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(sh0);
        hash.Add(sh1);
        hash.Add(sh2);
        hash.Add(sh3);
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL1";
}

// The same 4 coefficients of any kind, one number or one color each: harmonics of a single value, or of
// a color with its alpha.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL1<T> : IEquatable<SphericalHarmonicsL1<T>> where T : unmanaged, IEquatable<T>
{
    public const int Count = 4;

    public T sh0;
    public T sh1;
    public T sh2;
    public T sh3;

    public SphericalHarmonicsL1(T sh0, T sh1, T sh2, T sh3)
    {
        this.sh0 = sh0;
        this.sh1 = sh1;
        this.sh2 = sh2;
        this.sh3 = sh3;
    }

    // Every field is a T, laid out one after another, so the coefficients read as a run of them.
    public T this[int index]
    {
        get => MemoryMarshal.CreateReadOnlySpan(ref sh0, Count)[index];
        set => MemoryMarshal.CreateSpan(ref sh0, Count)[index] = value;
    }

    public static bool operator ==(SphericalHarmonicsL1<T> a, SphericalHarmonicsL1<T> b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL1<T> a, SphericalHarmonicsL1<T> b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL1<T> other && Equals(other);

    public bool Equals(SphericalHarmonicsL1<T> other)
    {
        var comparer = EqualityComparer<T>.Default;
        return comparer.Equals(sh0, other.sh0) && comparer.Equals(sh1, other.sh1) && comparer.Equals(sh2, other.sh2) &&
            comparer.Equals(sh3, other.sh3);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(sh0);
        hash.Add(sh1);
        hash.Add(sh2);
        hash.Add(sh3);
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL1<" + typeof(T).Name + ">";
}
