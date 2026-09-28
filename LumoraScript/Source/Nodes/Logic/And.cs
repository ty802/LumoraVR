// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Wasm;

namespace Lumora.Script;

// A bool vector goes lane by lane; a whole number bit by bit.
[Variadic]
[Node("Logic/And")]
public sealed class And : ValueNode
{
    [Wasm(WasmOp.I32And)] public static bool Run(bool a, bool b) => a & b;
    [Wasm(WasmOp.I32And, Lanes = 2)] public static bool2 Run(bool2 a, bool2 b) => a & b;
    [Wasm(WasmOp.I32And, Lanes = 3)] public static bool3 Run(bool3 a, bool3 b) => a & b;
    [Wasm(WasmOp.I32And, Lanes = 4)] public static bool4 Run(bool4 a, bool4 b) => a & b;
    [Wasm(WasmOp.I32And)] public static int Run(int a, int b) => a & b;
    [Wasm(WasmOp.I64And)] public static long Run(long a, long b) => a & b;
    [Wasm(WasmOp.I32And)] public static uint Run(uint a, uint b) => a & b;
    [Wasm(WasmOp.I64And)] public static ulong Run(ulong a, ulong b) => a & b;
}
