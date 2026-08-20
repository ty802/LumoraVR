// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

public static class ParticleMath
{
    // Rotation that takes local +Z onto forward with local +Y as close to up as it can get.
    //
    // Built here from the basis columns rather than through floatQ.LookRotation, which assembles the
    // matrix from basis ROWS and therefore hands back the inverse rotation. Anything facing-related
    // built on that ends up rotated wrong by exactly the amount that is hardest to notice until a
    // quad goes edge-on at an oblique angle. -xlinka
    public static floatQ FromForwardUp(in float3 forward, in float3 up)
    {
        var f = forward;
        float flen = f.Length;
        if (flen < 1e-6f)
            return floatQ.Identity;
        f /= flen;

        var r = float3.Cross(up, f);
        float rlen = r.Length;
        if (rlen < 1e-6f)
        {
            // Forward is parallel to up: any roll is as good as any other, pick a stable one.
            r = float3.Cross(MathF.Abs(f.y) > 0.9f ? float3.Forward : float3.Up, f);
            rlen = r.Length;
            if (rlen < 1e-6f)
                return floatQ.Identity;
        }
        r /= rlen;
        var u = float3.Cross(f, r);

        // Basis as columns: m[row, col], col0 = right, col1 = up, col2 = forward.
        float m00 = r.x, m10 = r.y, m20 = r.z;
        float m01 = u.x, m11 = u.y, m21 = u.z;
        float m02 = f.x, m12 = f.y, m22 = f.z;

        float trace = m00 + m11 + m22;
        floatQ q;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            q = new floatQ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            q = new floatQ(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            q = new floatQ((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            q = new floatQ((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
        }
        return q.Normalized;
    }

    // Smoothstep with a guard against a zero-width edge range.
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = System.Math.Clamp((x - edge0) / MathF.Max(edge1 - edge0, 1e-5f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Turn a per-particle seed into a stable value in [0, 1). Same seed, same value, forever.
    public static float Seed01(uint seed)
    {
        // One xorshift-multiply round so neighbouring seeds do not produce neighbouring values.
        seed ^= seed >> 16;
        seed *= 0x7FEB352Du;
        seed ^= seed >> 15;
        return (seed >> 8) * (1f / 16777216f);
    }

    // Blend from one direction toward another by weight, keeping the first one's magnitude.
    public static float3 BlendDirection(in float3 direction, in float3 target, float weight)
    {
        float magnitude = direction.Length;
        if (magnitude < 1e-6f)
            return target * 0f;
        var blended = direction / magnitude + (target - direction / magnitude) * System.Math.Clamp(weight, 0f, 1f);
        float len = blended.Length;
        return len > 1e-6f ? blended * (magnitude / len) : direction;
    }
}
