// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A value split into the channels the curve editor draws, and put back together. Built once per type,
// and the split and join never box, because the sampler runs them for every point of every curve
// on every redraw.
public static class CurveChannels<T>
{
    private delegate void SplitFn(ref T value, float[] into);
    private delegate T JoinFn(float[] from);

    private sealed class Spec
    {
        public string[] Labels = Array.Empty<string>();
        public CurveValueKind Kind = CurveValueKind.Text;
        public SplitFn Split = (ref T v, float[] a) => { };
        public JoinFn Join = a => default!;
    }

    private const float RadToDeg = 180f / MathF.PI;
    private const float DegToRad = MathF.PI / 180f;

    public static readonly string[] Labels;
    public static readonly CurveValueKind Kind;
    private static readonly SplitFn Splitter;
    private static readonly JoinFn Joiner;

    static CurveChannels()
    {
        var spec = Describe();
        Labels = spec.Labels;
        Kind = spec.Kind;
        Splitter = spec.Split;
        Joiner = spec.Join;
    }

    public static int Count => Labels.Length;

    public static void Split(T value, float[] into) => Splitter(ref value, into);

    public static T Join(float[] from) => Joiner(from);

    private static Spec Describe()
    {
        var t = typeof(T);
        if (t == typeof(float))
        {
            return new Spec
            {
                Labels = new[] { "Value" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => a[0] = Unsafe.As<T, float>(ref v),
                Join = a => { float f = a[0]; return Unsafe.As<float, T>(ref f); },
            };
        }
        if (t == typeof(double))
        {
            return new Spec
            {
                Labels = new[] { "Value" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => a[0] = (float)Unsafe.As<T, double>(ref v),
                Join = a => { double d = a[0]; return Unsafe.As<double, T>(ref d); },
            };
        }
        if (t == typeof(int))
        {
            return new Spec
            {
                Labels = new[] { "Value" },
                Kind = CurveValueKind.Whole,
                Split = (ref T v, float[] a) => a[0] = Unsafe.As<T, int>(ref v),
                Join = a => { int i = (int)MathF.Round(a[0]); return Unsafe.As<int, T>(ref i); },
            };
        }
        if (t == typeof(long))
        {
            return new Spec
            {
                Labels = new[] { "Value" },
                Kind = CurveValueKind.Whole,
                Split = (ref T v, float[] a) => a[0] = Unsafe.As<T, long>(ref v),
                Join = a => { long l = (long)MathF.Round(a[0]); return Unsafe.As<long, T>(ref l); },
            };
        }
        if (t == typeof(bool))
        {
            return new Spec
            {
                Labels = new[] { "On" },
                Kind = CurveValueKind.Flag,
                Split = (ref T v, float[] a) => a[0] = Unsafe.As<T, bool>(ref v) ? 1f : 0f,
                Join = a => { bool b = a[0] >= 0.5f; return Unsafe.As<bool, T>(ref b); },
            };
        }
        if (t == typeof(float2))
        {
            return new Spec
            {
                Labels = new[] { "X", "Y" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => { var f = Unsafe.As<T, float2>(ref v); a[0] = f.x; a[1] = f.y; },
                Join = a => { var f = new float2(a[0], a[1]); return Unsafe.As<float2, T>(ref f); },
            };
        }
        if (t == typeof(float3))
        {
            return new Spec
            {
                Labels = new[] { "X", "Y", "Z" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => { var f = Unsafe.As<T, float3>(ref v); a[0] = f.x; a[1] = f.y; a[2] = f.z; },
                Join = a => { var f = new float3(a[0], a[1], a[2]); return Unsafe.As<float3, T>(ref f); },
            };
        }
        if (t == typeof(float4))
        {
            return new Spec
            {
                Labels = new[] { "X", "Y", "Z", "W" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => { var f = Unsafe.As<T, float4>(ref v); a[0] = f.x; a[1] = f.y; a[2] = f.z; a[3] = f.w; },
                Join = a => { var f = new float4(a[0], a[1], a[2], a[3]); return Unsafe.As<float4, T>(ref f); },
            };
        }
        if (t == typeof(color))
        {
            return new Spec
            {
                Labels = new[] { "R", "G", "B", "A" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => { var c = Unsafe.As<T, color>(ref v); a[0] = c.r; a[1] = c.g; a[2] = c.b; a[3] = c.a; },
                Join = a => { var c = new color(a[0], a[1], a[2], a[3]); return Unsafe.As<color, T>(ref c); },
            };
        }
        if (t == typeof(colorHDR))
        {
            return new Spec
            {
                Labels = new[] { "R", "G", "B", "A" },
                Kind = CurveValueKind.Scalar,
                Split = (ref T v, float[] a) => { var c = Unsafe.As<T, colorHDR>(ref v); a[0] = c.r; a[1] = c.g; a[2] = c.b; a[3] = c.a; },
                Join = a => { var c = new colorHDR(a[0], a[1], a[2], a[3]); return Unsafe.As<colorHDR, T>(ref c); },
            };
        }
        if (t == typeof(floatQ))
        {
            // Degrees, in the order ToEuler reads them: x pitch, y yaw, z roll.
            return new Spec
            {
                Labels = new[] { "X", "Y", "Z" },
                Kind = CurveValueKind.Rotation,
                Split = (ref T v, float[] a) =>
                {
                    var e = Unsafe.As<T, floatQ>(ref v).ToEuler();
                    a[0] = e.x * RadToDeg;
                    a[1] = e.y * RadToDeg;
                    a[2] = e.z * RadToDeg;
                },
                Join = a =>
                {
                    var q = floatQ.Euler(new float3(a[0] * DegToRad, a[1] * DegToRad, a[2] * DegToRad));
                    return Unsafe.As<floatQ, T>(ref q);
                },
            };
        }
        return new Spec();
    }
}
