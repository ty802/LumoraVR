// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Wasm;

namespace Lumora.Script;

// Integer division by zero traps, the same as the C# body throwing; the call ends at this node. Vectors
// go lane by lane, and a number over a vector divides it by each lane. A span of time divided past the
// longest there is holds at it; a NaN length, such as zero over zero, is zero.
[Node("Math/Divide")]
public sealed class Divide : ValueNode
{
    [Wasm(WasmOp.I32DivS)] public static int Run(int a, int b) => a / b;
    [Wasm(WasmOp.I64DivS)] public static long Run(long a, long b) => a / b;
    [Wasm(WasmOp.F32Div)] public static float Run(float a, float b) => a / b;
    [Wasm(WasmOp.F64Div)] public static double Run(double a, double b) => a / b;
    [Wasm(WasmOp.F32Div, Lanes = 3)] public static float3 Run(float3 a, float3 b) => a / b;
    public static float3 Run(float3 a, float b) => a / b;
    [Wasm(WasmOp.F32Div, Lanes = 2)] public static float2 Run(float2 a, float2 b) => a / b;
    [Wasm(WasmOp.F32Div, Lanes = 4)] public static float4 Run(float4 a, float4 b) => a / b;
    public static float2 Run(float2 a, float b) => a / b;
    public static float4 Run(float4 a, float b) => a / b;
    public static float2 Run(float a, float2 b) => new(a / b.x, a / b.y);
    public static float3 Run(float a, float3 b) => new(a / b.x, a / b.y, a / b.z);
    public static float4 Run(float a, float4 b) => new(a / b.x, a / b.y, a / b.z, a / b.w);
    [Wasm(WasmOp.I32DivS, Lanes = 2)] public static int2 Run(int2 a, int2 b) => new(Run(a.x, b.x), Run(a.y, b.y));
    [Wasm(WasmOp.I32DivS, Lanes = 3)] public static int3 Run(int3 a, int3 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z));
    [Wasm(WasmOp.I32DivS, Lanes = 4)] public static int4 Run(int4 a, int4 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z), Run(a.w, b.w));
    public static int2 Run(int2 a, int b) => a / b;
    public static int3 Run(int3 a, int b) => a / b;
    public static int4 Run(int4 a, int b) => a / b;
    public static int2 Run(int a, int2 b) => new(Run(a, b.x), Run(a, b.y));
    public static int3 Run(int a, int3 b) => new(Run(a, b.x), Run(a, b.y), Run(a, b.z));
    public static int4 Run(int a, int4 b) => new(Run(a, b.x), Run(a, b.y), Run(a, b.z), Run(a, b.w));
    public static color Run(color a, float b) => a / b;
    public static colorHDR Run(colorHDR a, float b) => a / b;
    [Wasm(WasmOp.I32DivU)] public static uint Run(uint a, uint b) => a / b;
    [Wasm(WasmOp.I64DivU)] public static ulong Run(ulong a, ulong b) => a / b;
    public static TimeSpan Run(TimeSpan a, double b) => ClampedTime.Span(a.Ticks / b);
}
