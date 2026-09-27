// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Wasm;

// Every arithmetic NaN leaves the engine as the one canonical pattern. The specification allows any
// quiet NaN there, and hardware disagrees about which one it produces, so without this two peers could
// compute different bits from the same script. Bit-level operations (abs, neg, copysign, loads, stores,
// reinterpretation) never pass through here and keep their payloads exactly. -xlinka
internal static class WasmNumerics
{
    public const uint CanonicalNaN32 = 0x7FC00000;
    public const ulong CanonicalNaN64 = 0x7FF8000000000000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float F32(ulong bits) => BitConverter.UInt32BitsToSingle((uint)bits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double F64(ulong bits) => BitConverter.UInt64BitsToDouble(bits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bits(float value) => float.IsNaN(value) ? CanonicalNaN32 : BitConverter.SingleToUInt32Bits(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bits(double value) => double.IsNaN(value) ? CanonicalNaN64 : BitConverter.DoubleToUInt64Bits(value);

    public static float Min(float a, float b)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
            return float.NaN;
        if (a == 0 && b == 0)
            return float.IsNegative(a) ? a : b;
        return a < b ? a : b;
    }

    public static float Max(float a, float b)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
            return float.NaN;
        if (a == 0 && b == 0)
            return float.IsNegative(a) ? b : a;
        return a > b ? a : b;
    }

    public static double Min(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
            return double.NaN;
        if (a == 0 && b == 0)
            return double.IsNegative(a) ? a : b;
        return a < b ? a : b;
    }

    public static double Max(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
            return double.NaN;
        if (a == 0 && b == 0)
            return double.IsNegative(a) ? b : a;
        return a > b ? a : b;
    }

    // Rounding that lands on zero keeps the sign of its input (-0.3 rounds to -0), which is what the
    // specification asks for and what a naive helper can lose.
    public static float Nearest(float x) => KeepZeroSign(MathF.Round(x, MidpointRounding.ToEven), x);
    public static float Ceil(float x) => KeepZeroSign(MathF.Ceiling(x), x);
    public static float Floor(float x) => KeepZeroSign(MathF.Floor(x), x);
    public static float Trunc(float x) => KeepZeroSign(MathF.Truncate(x), x);
    public static double Nearest(double x) => KeepZeroSign(Math.Round(x, MidpointRounding.ToEven), x);
    public static double Ceil(double x) => KeepZeroSign(Math.Ceiling(x), x);
    public static double Floor(double x) => KeepZeroSign(Math.Floor(x), x);
    public static double Trunc(double x) => KeepZeroSign(Math.Truncate(x), x);

    private static float KeepZeroSign(float result, float input)
        => result == 0 ? (float.IsNegative(input) ? -0.0f : 0.0f) : result;

    private static double KeepZeroSign(double result, double input)
        => result == 0 ? (double.IsNegative(input) ? -0.0 : 0.0) : result;

    // Unsigned 64-bit to float, rounded once. Halving with the dropped bit folded back in keeps the
    // sticky information a straight conversion through a signed value would lose.
    public static float U64ToF32(ulong x)
    {
        if ((long)x >= 0)
            return (long)x;
        ulong half = (x >> 1) | (x & 1);
        return (float)(long)half * 2f;
    }

    public static double U64ToF64(ulong x)
    {
        if ((long)x >= 0)
            return (long)x;
        ulong half = (x >> 1) | (x & 1);
        return (double)(long)half * 2.0;
    }

    private static WasmTrapException Overflow() => new(WasmTrapCode.IntegerOverflow);
    private static WasmTrapException BadConversion() => new(WasmTrapCode.InvalidConversionToInteger);

    public static int TruncF32ToI32(float x)
    {
        if (float.IsNaN(x))
            throw BadConversion();
        if (!(x >= -2147483648.0f && x < 2147483648.0f))
            throw Overflow();
        return (int)x;
    }

    public static uint TruncF32ToU32(float x)
    {
        if (float.IsNaN(x))
            throw BadConversion();
        if (!(x > -1.0f && x < 4294967296.0f))
            throw Overflow();
        return (uint)(long)x;
    }

    public static int TruncF64ToI32(double x)
    {
        if (double.IsNaN(x))
            throw BadConversion();
        if (!(x > -2147483649.0 && x < 2147483648.0))
            throw Overflow();
        return (int)x;
    }

    public static uint TruncF64ToU32(double x)
    {
        if (double.IsNaN(x))
            throw BadConversion();
        if (!(x > -1.0 && x < 4294967296.0))
            throw Overflow();
        return (uint)(long)x;
    }

    public static long TruncF32ToI64(float x)
    {
        if (float.IsNaN(x))
            throw BadConversion();
        if (!(x >= -9223372036854775808.0f && x < 9223372036854775808.0f))
            throw Overflow();
        return (long)x;
    }

    public static ulong TruncF32ToU64(float x)
    {
        if (float.IsNaN(x))
            throw BadConversion();
        if (!(x > -1.0f && x < 18446744073709551616.0f))
            throw Overflow();
        return DoubleToU64(x);
    }

    public static long TruncF64ToI64(double x)
    {
        if (double.IsNaN(x))
            throw BadConversion();
        if (!(x >= -9223372036854775808.0 && x < 9223372036854775808.0))
            throw Overflow();
        return (long)x;
    }

    public static ulong TruncF64ToU64(double x)
    {
        if (double.IsNaN(x))
            throw BadConversion();
        if (!(x > -1.0 && x < 18446744073709551616.0))
            throw Overflow();
        return DoubleToU64(x);
    }

    // x is already known to be in (-1, 2^64).
    private static ulong DoubleToU64(double x)
    {
        if (x >= 9223372036854775808.0)
            return (ulong)(long)(x - 9223372036854775808.0) | 0x8000000000000000UL;
        return x <= 0 ? 0UL : (ulong)(long)x;
    }

    public static int SatF32ToI32(float x)
    {
        if (float.IsNaN(x))
            return 0;
        if (x < -2147483648.0f)
            return int.MinValue;
        if (x >= 2147483648.0f)
            return int.MaxValue;
        return (int)x;
    }

    public static uint SatF32ToU32(float x)
    {
        if (float.IsNaN(x) || x <= -1.0f)
            return 0;
        if (x >= 4294967296.0f)
            return uint.MaxValue;
        return x <= 0 ? 0u : (uint)(long)x;
    }

    public static int SatF64ToI32(double x)
    {
        if (double.IsNaN(x))
            return 0;
        if (x <= -2147483649.0)
            return int.MinValue;
        if (x >= 2147483648.0)
            return int.MaxValue;
        return (int)x;
    }

    public static uint SatF64ToU32(double x)
    {
        if (double.IsNaN(x) || x <= -1.0)
            return 0;
        if (x >= 4294967296.0)
            return uint.MaxValue;
        return x <= 0 ? 0u : (uint)(long)x;
    }

    public static long SatF32ToI64(float x)
    {
        if (float.IsNaN(x))
            return 0;
        if (x < -9223372036854775808.0f)
            return long.MinValue;
        if (x >= 9223372036854775808.0f)
            return long.MaxValue;
        return (long)x;
    }

    public static ulong SatF32ToU64(float x)
    {
        if (float.IsNaN(x) || x <= -1.0f)
            return 0;
        if (x >= 18446744073709551616.0f)
            return ulong.MaxValue;
        return DoubleToU64(x);
    }

    public static long SatF64ToI64(double x)
    {
        if (double.IsNaN(x))
            return 0;
        if (x < -9223372036854775808.0)
            return long.MinValue;
        if (x >= 9223372036854775808.0)
            return long.MaxValue;
        return (long)x;
    }

    public static ulong SatF64ToU64(double x)
    {
        if (double.IsNaN(x) || x <= -1.0)
            return 0;
        if (x >= 18446744073709551616.0)
            return ulong.MaxValue;
        return DoubleToU64(x);
    }
}
