// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

// Deterministic seeded random source. Same seed, same call sequence, same numbers, on every machine
// and every run - which is the whole point: simulations that run per peer (particles, cloth) stay
// visually identical across a session without shipping a single byte of state over the wire, as long
// as they are driven from a shared seed. System.Random gives no such guarantee across runtimes.
//
// PCG-XSH-RR (32-bit output, 64-bit state): one multiply-add per draw, passes the usual statistical
// batteries, and unlike a raw LCG the low bits are not garbage - which matters because half the
// shape sampling below uses them. -xlinka
public sealed class SeededRandom
{
    private const ulong Multiplier = 6364136223846793005ul;
    private const ulong Increment = 1442695040888963407ul;

    private ulong _state;

    public SeededRandom(int? seed = null)
        => Reseed(seed ?? Environment.TickCount);

    // two generators reseeded alike produce identical draws
    public void Reseed(int seed)
    {
        _state = 0ul;
        NextUInt();
        _state += (ulong)(uint)seed * 0x9E3779B97F4A7C15ul;
        NextUInt();
    }

    // the raw draw everything else below derives from
    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * Multiplier + Increment;
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    // uniform in [0, 1)
    public float Value => (NextUInt() >> 8) * (1f / 16777216f);

    // uniform in [-1, 1)
    public float Signed => Value * 2f - 1f;

    public bool Bool => (NextUInt() & 1u) != 0u;

    public float Range(float min, float max) => min + (max - min) * Value;

    // [min, max); returns min when the range is empty
    public int Range(int min, int max)
    {
        if (max <= min)
            return min;
        return min + (int)(NextUInt() % (uint)(max - min));
    }

    public float2 Range(float2 min, float2 max) => new float2(Range(min.x, max.x), Range(min.y, max.y));

    public float3 Range(float3 min, float3 max)
        => new float3(Range(min.x, max.x), Range(min.y, max.y), Range(min.z, max.z));

    public color Range(color min, color max)
    {
        float t = Value;
        return color.Lerp(min, max, t);
    }

    public colorHDR Range(colorHDR min, colorHDR max)
    {
        float t = Value;
        return colorHDR.Lerp(min, max, t);
    }

    // spans [-0.5, 0.5] on every axis
    public float3 InsideUnitCube => new float3(Value - 0.5f, Value - 0.5f, Value - 0.5f);

    // uniform point on the surface of the [-0.5, 0.5] cube, with the face normal it landed on
    public void OnUnitCubeWithNormal(out float3 point, out float3 normal)
    {
        int face = Range(0, 6);
        int axis = face >> 1;
        float sign = (face & 1) == 0 ? 0.5f : -0.5f;
        float a = Value - 0.5f;
        float b = Value - 0.5f;
        point = axis switch
        {
            0 => new float3(sign, a, b),
            1 => new float3(a, sign, b),
            _ => new float3(a, b, sign),
        };
        normal = axis switch
        {
            0 => new float3(sign > 0f ? 1f : -1f, 0f, 0f),
            1 => new float3(0f, sign > 0f ? 1f : -1f, 0f),
            _ => new float3(0f, 0f, sign > 0f ? 1f : -1f),
        };
    }

    // analytic (no rejection loop), so the cost is fixed
    public float3 OnUnitSphere
    {
        get
        {
            float z = Signed;
            float theta = Value * MathF.PI * 2f;
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
            return new float3(MathF.Cos(theta) * r, MathF.Sin(theta) * r, z);
        }
    }

    // cube-root radius keeps the density even
    public float3 InsideUnitSphere => OnUnitSphere * MathF.Cbrt(Value);

    public float2 OnUnitCircle
    {
        get
        {
            float a = Value * MathF.PI * 2f;
            return new float2(MathF.Cos(a), MathF.Sin(a));
        }
    }

    // sqrt radius keeps the density even
    public float2 InsideUnitCircle => OnUnitCircle * MathF.Sqrt(Value);

    // Y-up cone, apex at -height/2, base radius at +height/2. Radius scales with height so the
    // sampling is even through the volume rather than bunched at the tip.
    public float3 InsideCone(float height, float baseRadius)
    {
        float t = Value;
        float2 disc = InsideUnitCircle * (baseRadius * t);
        return new float3(disc.x, (t - 0.5f) * height, disc.y);
    }

    // the lateral surface of the same cone
    public float3 OnCone(float height, float baseRadius)
    {
        float t = MathF.Sqrt(Value);
        float2 disc = OnUnitCircle * (baseRadius * t);
        return new float3(disc.x, (t - 0.5f) * height, disc.y);
    }

    public float3 BarycentricCoordinate
    {
        get
        {
            float u = Value;
            float v = Value;
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }
            return new float3(u, v, 1f - u - v);
        }
    }
}
