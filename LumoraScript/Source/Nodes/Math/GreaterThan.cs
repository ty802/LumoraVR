// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Wasm;

namespace Lumora.Script;

// Dates compare by their ticks, whatever their kinds.
[Node("Math/Compare/Greater Than")]
public sealed class GreaterThan : ValueNode
{
    [Wasm(WasmOp.I32GtS)] public static bool Run(int a, int b) => a > b;
    [Wasm(WasmOp.I64GtS)] public static bool Run(long a, long b) => a > b;
    [Wasm(WasmOp.F32Gt)] public static bool Run(float a, float b) => a > b;
    [Wasm(WasmOp.F64Gt)] public static bool Run(double a, double b) => a > b;
    [Wasm(WasmOp.I32GtU)] public static bool Run(uint a, uint b) => a > b;
    [Wasm(WasmOp.I64GtU)] public static bool Run(ulong a, ulong b) => a > b;
    public static bool Run(DateTime a, DateTime b) => a > b;
    public static bool Run(TimeSpan a, TimeSpan b) => a > b;
}
