// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// vec_avx.h: Copyright (c) 2018 Mozilla, Copyright (c) 2012-2017 Jean-Marc Valin.
// See LICENSE-RNNoise.txt.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lumora.Audio;

// Kernel switches. Production code leaves both on; the test harness turns them off to check and
// time the fallbacks on one machine.
internal static class RnnoiseCpu
{
    internal static bool AllowVectors = true;
    internal static bool AllowX86 = true;

    internal static bool Vectors => AllowVectors && Vector128.IsHardwareAccelerated;
    internal static bool Avx2Dot => AllowVectors && AllowX86 && Avx2.IsSupported;
    internal static bool SseReciprocal => AllowX86 && Sse.IsSupported;
}

// tanh and sigmoid as RNNoise's SSE build computes them: a rational fit clamped to the output
// range, the division done as a multiply by _mm_rcp_ps, every fmadd a multiply then an add.
// rcpps is a table lookup whose low bits differ between CPU vendors, so only the instruction
// itself reproduces the C library bit for bit. Without SSE the reciprocal is an exact division,
// within 1.5 * 2^-12 of it. -xlinka
internal static class RnnoiseActivations
{
    private const float TanhN0 = 952.52801514f;
    private const float TanhN1 = 96.39235687f;
    private const float TanhN2 = 0.60863042f;
    private const float TanhD0 = 952.72399902f;
    private const float TanhD1 = 413.36801147f;
    private const float TanhD2 = 11.88600922f;

    private const float SigmoidN0 = 238.13200378f;
    private const float SigmoidN1 = 6.02452230f;
    private const float SigmoidN2 = 0.00950985f;
    private const float SigmoidD0 = 952.72399902f;
    private const float SigmoidD1 = 103.34200287f;
    private const float SigmoidD2 = 0.74287558f;

    internal static void Tanh(Span<float> values)
    {
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float v = ref MemoryMarshal.GetReference(values);
            for (; i + 4 <= values.Length; i += 4)
                TanhVector(Vector128.LoadUnsafe(ref v, (nuint)i)).StoreUnsafe(ref v, (nuint)i);
        }
        for (; i < values.Length; i++)
            values[i] = TanhScalar(values[i]);
    }

    internal static void Sigmoid(Span<float> values)
    {
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float v = ref MemoryMarshal.GetReference(values);
            for (; i + 4 <= values.Length; i += 4)
                SigmoidVector(Vector128.LoadUnsafe(ref v, (nuint)i)).StoreUnsafe(ref v, (nuint)i);
        }
        for (; i < values.Length; i++)
            values[i] = SigmoidScalar(values[i]);
    }

    private static Vector128<float> TanhVector(Vector128<float> x)
    {
        Vector128<float> x2 = x * x;
        Vector128<float> num = (Vector128.Create(TanhN2) * x2 + Vector128.Create(TanhN1)) * x2 + Vector128.Create(TanhN0);
        Vector128<float> den = (Vector128.Create(TanhD2) * x2 + Vector128.Create(TanhD1)) * x2 + Vector128.Create(TanhD0);
        num = num * x;
        num = num * Reciprocal(den);
        return Vector128.Max(Vector128.Create(-1f), Vector128.Min(Vector128<float>.One, num));
    }

    private static Vector128<float> SigmoidVector(Vector128<float> x)
    {
        Vector128<float> x2 = x * x;
        Vector128<float> num = (Vector128.Create(SigmoidN2) * x2 + Vector128.Create(SigmoidN1)) * x2 + Vector128.Create(SigmoidN0);
        Vector128<float> den = (Vector128.Create(SigmoidD2) * x2 + Vector128.Create(SigmoidD1)) * x2 + Vector128.Create(SigmoidD0);
        num = num * x;
        num = num * Reciprocal(den) + Vector128.Create(0.5f);
        return Vector128.Max(Vector128<float>.Zero, Vector128.Min(Vector128<float>.One, num));
    }

    private static float TanhScalar(float x)
    {
        float x2 = x * x;
        float num = (TanhN2 * x2 + TanhN1) * x2 + TanhN0;
        float den = (TanhD2 * x2 + TanhD1) * x2 + TanhD0;
        num = num * x;
        num = num * Reciprocal(den);
        return MathF.Max(-1f, MathF.Min(1f, num));
    }

    private static float SigmoidScalar(float x)
    {
        float x2 = x * x;
        float num = (SigmoidN2 * x2 + SigmoidN1) * x2 + SigmoidN0;
        float den = (SigmoidD2 * x2 + SigmoidD1) * x2 + SigmoidD0;
        num = num * x;
        num = num * Reciprocal(den) + 0.5f;
        return MathF.Max(0f, MathF.Min(1f, num));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Reciprocal(Vector128<float> d)
        => RnnoiseCpu.SseReciprocal ? Sse.Reciprocal(d) : Vector128<float>.One / d;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Reciprocal(float d)
        => RnnoiseCpu.SseReciprocal ? Sse.Reciprocal(Vector128.Create(d)).ToScalar() : 1f / d;
}
