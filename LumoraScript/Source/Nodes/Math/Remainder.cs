// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Wasm;

namespace Lumora.Script;

// The signed forms are not inlined: the smallest integer over -1 is 0 here, where C# throws and rem_s
// does not. An integer remainder by zero traps. Vectors go lane by lane.
[Node("Math/Remainder")]
public sealed class Remainder : ValueNode
{
    public static int Run(int a, int b) => b == -1 ? 0 : a % b;
    public static long Run(long a, long b) => b == -1 ? 0 : a % b;
    public static float Run(float a, float b) => a % b;
    public static double Run(double a, double b) => a % b;
    public static float2 Run(float2 a, float2 b) => new(Run(a.x, b.x), Run(a.y, b.y));
    public static float3 Run(float3 a, float3 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z));
    public static float4 Run(float4 a, float4 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z), Run(a.w, b.w));
    public static int2 Run(int2 a, int2 b) => new(Run(a.x, b.x), Run(a.y, b.y));
    public static int3 Run(int3 a, int3 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z));
    public static int4 Run(int4 a, int4 b) => new(Run(a.x, b.x), Run(a.y, b.y), Run(a.z, b.z), Run(a.w, b.w));
    [Wasm(WasmOp.I32RemU)] public static uint Run(uint a, uint b) => a % b;
    [Wasm(WasmOp.I64RemU)] public static ulong Run(ulong a, ulong b) => a % b;
}
