// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Wasm;

namespace Lumora.Script;

// Component-wise for two vectors or two colors; a vector or a color and a number scales it. A span of
// time scaled past the longest there is holds at it, and a NaN length is zero.
[Variadic]
[Node("Math/Multiply")]
public sealed class Multiply : ValueNode
{
    [Wasm(WasmOp.I32Mul)] public static int Run(int a, int b) => unchecked(a * b);
    [Wasm(WasmOp.I64Mul)] public static long Run(long a, long b) => unchecked(a * b);
    [Wasm(WasmOp.F32Mul)] public static float Run(float a, float b) => a * b;
    [Wasm(WasmOp.F64Mul)] public static double Run(double a, double b) => a * b;
    [Wasm(WasmOp.F32Mul, Lanes = 2)] public static float2 Run(float2 a, float2 b) => a * b;
    [Wasm(WasmOp.F32Mul, Lanes = 3)] public static float3 Run(float3 a, float3 b) => a * b;
    [Wasm(WasmOp.F32Mul, Lanes = 4)] public static float4 Run(float4 a, float4 b) => a * b;
    [WasmBody(nameof(ScaleTwo))] public static float2 Run(float2 a, float b) => a * b;
    [WasmBody(nameof(ScaleThree))] public static float3 Run(float3 a, float b) => a * b;
    [WasmBody(nameof(ScaleFour))] public static float4 Run(float4 a, float b) => a * b;
    [WasmBody(nameof(ByTwo))] public static float2 Run(float a, float2 b) => new(a * b.x, a * b.y);
    [WasmBody(nameof(ByThree))] public static float3 Run(float a, float3 b) => a * b;
    [WasmBody(nameof(ByFour))] public static float4 Run(float a, float4 b) => a * b;
    [Wasm(WasmOp.I32Mul, Lanes = 2)] public static int2 Run(int2 a, int2 b) => a * b;
    [Wasm(WasmOp.I32Mul, Lanes = 3)] public static int3 Run(int3 a, int3 b) => a * b;
    [Wasm(WasmOp.I32Mul, Lanes = 4)] public static int4 Run(int4 a, int4 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z), Run(a.w, b.w));
    public static int2 Run(int2 a, int b) => a * b;
    public static int3 Run(int3 a, int b) => a * b;
    public static int4 Run(int4 a, int b) => a * b;
    public static int2 Run(int a, int2 b) => a * b;
    public static int3 Run(int a, int3 b) => a * b;
    public static int4 Run(int a, int4 b) => a * b;
    [Wasm(WasmOp.F32Mul, Lanes = 4)] public static color Run(color a, color b) => a * b;
    [Wasm(WasmOp.F32Mul, Lanes = 4)] public static colorHDR Run(colorHDR a, colorHDR b) => a * b;
    public static color Run(color a, float b) => a * b;
    public static color Run(float a, color b) => a * b;
    public static colorHDR Run(colorHDR a, float b) => a * b;
    public static colorHDR Run(float a, colorHDR b) => a * b;
    [Wasm(WasmOp.I32Mul)] public static uint Run(uint a, uint b) => unchecked(a * b);
    [Wasm(WasmOp.I64Mul)] public static ulong Run(ulong a, ulong b) => unchecked(a * b);
    public static TimeSpan Run(TimeSpan a, double b) => ClampedTime.Span(a.Ticks * b);

    // Each lane of the vector times the number, the vector's lane first or the number first as the body has it.
    private static void Scaled(WasmBodyWriter w, int lanes, int vector)
    {
        for (int lane = 0; lane < lanes; lane++)
        {
            if (vector == 0)
                w.In(0, lane).In(1, 0);
            else
                w.In(0, 0).In(1, lane);
            w.Op(WasmOp.F32Mul).Out(0, lane);
        }
    }

    private static void ScaleTwo(WasmBodyWriter w) => Scaled(w, 2, 0);
    private static void ScaleThree(WasmBodyWriter w) => Scaled(w, 3, 0);
    private static void ScaleFour(WasmBodyWriter w) => Scaled(w, 4, 0);
    private static void ByTwo(WasmBodyWriter w) => Scaled(w, 2, 1);
    private static void ByThree(WasmBodyWriter w) => Scaled(w, 3, 1);
    private static void ByFour(WasmBodyWriter w) => Scaled(w, 4, 1);
}
