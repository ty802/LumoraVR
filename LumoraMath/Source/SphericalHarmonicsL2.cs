// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lumora.Core.Math;

// Spherical harmonics through band 2: 9 RGB coefficients, the usual form for light arriving from every
// direction at a point.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL2 : IEquatable<SphericalHarmonicsL2>
{
    public const int Count = 9;

    public float3 sh0;
    public float3 sh1;
    public float3 sh2;
    public float3 sh3;
    public float3 sh4;
    public float3 sh5;
    public float3 sh6;
    public float3 sh7;
    public float3 sh8;

    public SphericalHarmonicsL2(float3 sh0, float3 sh1, float3 sh2, float3 sh3, float3 sh4, float3 sh5, float3 sh6, float3 sh7, float3 sh8)
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
            _ => throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL2 index: {index}")
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
                default: throw new IndexOutOfRangeException($"Invalid SphericalHarmonicsL2 index: {index}");
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

    public static SphericalHarmonicsL2 operator +(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b) => new(a.sh0 + b.sh0, a.sh1 + b.sh1, a.sh2 + b.sh2, a.sh3 + b.sh3, a.sh4 + b.sh4, a.sh5 + b.sh5, a.sh6 + b.sh6, a.sh7 + b.sh7, a.sh8 + b.sh8);
    public static SphericalHarmonicsL2 operator *(SphericalHarmonicsL2 a, float s) => new(a.sh0 * s, a.sh1 * s, a.sh2 * s, a.sh3 * s, a.sh4 * s, a.sh5 * s, a.sh6 * s, a.sh7 * s, a.sh8 * s);

    public static bool operator ==(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL2 other && Equals(other);
    public bool Equals(SphericalHarmonicsL2 other) => sh0 == other.sh0 && sh1 == other.sh1 && sh2 == other.sh2 && sh3 == other.sh3 && sh4 == other.sh4 && sh5 == other.sh5 && sh6 == other.sh6 && sh7 == other.sh7 && sh8 == other.sh8;

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
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL2";
}

// The same 9 coefficients of any kind, one number or one color each: harmonics of a single value, or of
// a color with its alpha.
[StructLayout(LayoutKind.Sequential)]
public struct SphericalHarmonicsL2<T> : IEquatable<SphericalHarmonicsL2<T>> where T : unmanaged, IEquatable<T>
{
    public const int Count = 9;

    public T sh0;
    public T sh1;
    public T sh2;
    public T sh3;
    public T sh4;
    public T sh5;
    public T sh6;
    public T sh7;
    public T sh8;

    public SphericalHarmonicsL2(T sh0, T sh1, T sh2, T sh3, T sh4, T sh5, T sh6, T sh7, T sh8)
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
    }

    // Every field is a T, laid out one after another, so the coefficients read as a run of them.
    public T this[int index]
    {
        get => MemoryMarshal.CreateReadOnlySpan(ref sh0, Count)[index];
        set => MemoryMarshal.CreateSpan(ref sh0, Count)[index] = value;
    }

    public static bool operator ==(SphericalHarmonicsL2<T> a, SphericalHarmonicsL2<T> b) => a.Equals(b);
    public static bool operator !=(SphericalHarmonicsL2<T> a, SphericalHarmonicsL2<T> b) => !a.Equals(b);

    public override bool Equals(object? obj) => obj is SphericalHarmonicsL2<T> other && Equals(other);

    public bool Equals(SphericalHarmonicsL2<T> other)
    {
        var comparer = EqualityComparer<T>.Default;
        return comparer.Equals(sh0, other.sh0) && comparer.Equals(sh1, other.sh1) && comparer.Equals(sh2, other.sh2) &&
            comparer.Equals(sh3, other.sh3) && comparer.Equals(sh4, other.sh4) && comparer.Equals(sh5, other.sh5) &&
            comparer.Equals(sh6, other.sh6) && comparer.Equals(sh7, other.sh7) && comparer.Equals(sh8, other.sh8);
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
        return hash.ToHashCode();
    }

    public override string ToString() => "SphericalHarmonicsL2<" + typeof(T).Name + ">";
}
