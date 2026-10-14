// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Wasm;

namespace Lumora.Script;

[Node("Math/Vectors/Length")]
public sealed class VectorLength : ValueNode
{
    [WasmBody(nameof(Two))] [return: Port("length")] public static float Run(float2 vector) => vector.Length;
    [WasmBody(nameof(Three))] [return: Port("length")] public static float Run(float3 vector) => vector.Length;
    [WasmBody(nameof(Four))] [return: Port("length")] public static float Run(float4 vector) => vector.Length;

    // The root of the squares summed in lane order, as Length does it.
    private static void Lanes(WasmBodyWriter w, int lanes)
    {
        for (int lane = 0; lane < lanes; lane++)
        {
            w.In(0, lane).In(0, lane).Op(WasmOp.F32Mul);
            if (lane > 0)
                w.Op(WasmOp.F32Add);
        }
        w.Op(WasmOp.F32Sqrt).Out(0, 0);
    }

    private static void Two(WasmBodyWriter w) => Lanes(w, 2);
    private static void Three(WasmBodyWriter w) => Lanes(w, 3);
    private static void Four(WasmBodyWriter w) => Lanes(w, 4);
}
