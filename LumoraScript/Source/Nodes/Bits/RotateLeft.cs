// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Wasm;

namespace Lumora.Script;

// Moves every bit up count places, the ones pushed off the top coming back in at the bottom. The
// count goes around the width, so -1 turns a 32-bit number by 31.
[Node("Math/Bits/Rotate Left")]
public sealed class RotateLeft : ValueNode
{
    [Wasm(WasmOp.I32Rotl)] public static int Run(int value, int count) => int.RotateLeft(value, count);
    [Wasm(WasmOp.I64Rotl)] public static long Run(long value, long count) => long.RotateLeft(value, (int)count);
    [Wasm(WasmOp.I32Rotl)] public static uint Run(uint value, uint count) => uint.RotateLeft(value, (int)count);
    [Wasm(WasmOp.I64Rotl)] public static ulong Run(ulong value, ulong count) => ulong.RotateLeft(value, (int)count);
}
