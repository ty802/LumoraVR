// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// One sending shape frozen into world space for a frame: a sphere (A == B) or a capsule round the segment A..B,
// with what it sends, whose it is and how fast it moved. The contact pass tests receivers against these and never
// goes back to a slot.
public struct ContactShape
{
    public float3 A;
    public float3 B;
    public float Radius;
    public float3 BoundsMin;
    public float3 BoundsMax;
    public float3 Velocity;

    public int[] Tags;
    public ulong TagMask;

    // Who it belongs to: the user wearing it, or else the object it is part of. Null is the world.
    public object? Owner;

    // On an avatar rather than a world object.
    public bool OnAvatar;

    public static ContactShape Make(in float3 a, in float3 b, float radius, int[] tags, ulong mask, object? owner, bool onAvatar, in float3 velocity)
    {
        var shape = new ContactShape
        {
            A = a,
            B = b,
            Radius = radius,
            Tags = tags,
            TagMask = mask,
            Owner = owner,
            OnAvatar = onAvatar,
            Velocity = velocity,
        };
        shape.BoundsMin = new float3(MathF.Min(a.x, b.x) - radius, MathF.Min(a.y, b.y) - radius, MathF.Min(a.z, b.z) - radius);
        shape.BoundsMax = new float3(MathF.Max(a.x, b.x) + radius, MathF.Max(a.y, b.y) + radius, MathF.Max(a.z, b.z) + radius);
        return shape;
    }

    // A sphere, or a capsule along the turned local Y whose Height is end to end, placed off an anchor slot. Lengths
    // scale with the anchor.
    public static bool Place(Slot anchor, float radius, float height, in float3 offset, in floatQ rotation, out float3 a, out float3 b, out float worldRadius)
    {
        var gs = anchor.GlobalScale;
        float scale = (MathF.Abs(gs.x) + MathF.Abs(gs.y) + MathF.Abs(gs.z)) / 3f;
        worldRadius = MathF.Max(radius, 0f) * scale;
        float half = MathF.Max(0f, height * 0.5f - radius);
        if (half <= 1e-6f)
        {
            a = anchor.LocalPointToGlobal(offset);
            b = a;
        }
        else
        {
            var along = rotation * float3.Up * half;
            a = anchor.LocalPointToGlobal(offset + along);
            b = anchor.LocalPointToGlobal(offset - along);
        }
        return worldRadius > 0f && float.IsFinite(a.x) && float.IsFinite(b.x);
    }

    // The nearest points of segments a0..a1 and b0..b1 (either may be a point).
    public static void Closest(in float3 a0, in float3 a1, in float3 b0, in float3 b1, out float3 onA, out float3 onB)
    {
        float3 d1 = a1 - a0;
        float3 d2 = b1 - b0;
        float3 r = a0 - b0;
        float aa = float3.Dot(d1, d1);
        float ee = float3.Dot(d2, d2);
        float f = float3.Dot(d2, r);
        float s, t;
        if (aa <= 1e-12f && ee <= 1e-12f)
        {
            s = 0f;
            t = 0f;
        }
        else if (aa <= 1e-12f)
        {
            s = 0f;
            t = System.Math.Clamp(f / ee, 0f, 1f);
        }
        else
        {
            float c = float3.Dot(d1, r);
            if (ee <= 1e-12f)
            {
                t = 0f;
                s = System.Math.Clamp(-c / aa, 0f, 1f);
            }
            else
            {
                float bb = float3.Dot(d1, d2);
                float denominator = aa * ee - bb * bb;
                s = denominator > 1e-12f ? System.Math.Clamp((bb * f - c * ee) / denominator, 0f, 1f) : 0f;
                t = (bb * s + f) / ee;
                if (t < 0f)
                {
                    t = 0f;
                    s = System.Math.Clamp(-c / aa, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = System.Math.Clamp((bb - c) / aa, 0f, 1f);
                }
            }
        }
        onA = a0 + d1 * s;
        onB = b0 + d2 * t;
    }
}
