// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Wasm;

namespace Lumora.Script;

// The instructions a [Wasm] overload may name: plain per-lane numeric operations, no memory, no
// control, no calls. Checked against the overload's types when the registry reads it, so a node that
// names the wrong instruction fails at startup instead of producing a module the validator rejects.
internal static class InlineOps
{
    internal readonly record struct Op(int Arity, WasmValueType Operand, WasmValueType Result);

    private static readonly Dictionary<byte, Op> _ops = Build();

    public static bool TryGet(byte opcode, out Op op) => _ops.TryGetValue(opcode, out op);

    private static Dictionary<byte, Op> Build()
    {
        var ops = new Dictionary<byte, Op>();
        const WasmValueType i32 = WasmValueType.I32, i64 = WasmValueType.I64, f32 = WasmValueType.F32, f64 = WasmValueType.F64;

        void Add(WasmValueType operand, WasmValueType result, int arity, params byte[] opcodes)
        {
            foreach (var opcode in opcodes)
                ops.Add(opcode, new Op(arity, operand, result));
        }

        Add(i32, i32, 2, WasmOp.I32Add, WasmOp.I32Sub, WasmOp.I32Mul, WasmOp.I32DivS, WasmOp.I32DivU, WasmOp.I32RemS,
            WasmOp.I32RemU, WasmOp.I32And, WasmOp.I32Or, WasmOp.I32Xor, WasmOp.I32Shl, WasmOp.I32ShrS, WasmOp.I32ShrU,
            WasmOp.I32Rotl, WasmOp.I32Rotr, WasmOp.I32Eq, WasmOp.I32Ne, WasmOp.I32LtS, WasmOp.I32LtU, WasmOp.I32GtS,
            WasmOp.I32GtU, WasmOp.I32LeS, WasmOp.I32LeU, WasmOp.I32GeS, WasmOp.I32GeU);
        Add(i32, i32, 1, WasmOp.I32Eqz, WasmOp.I32Clz, WasmOp.I32Ctz, WasmOp.I32Popcnt);

        Add(i64, i64, 2, WasmOp.I64Add, WasmOp.I64Sub, WasmOp.I64Mul, WasmOp.I64DivS, WasmOp.I64DivU, WasmOp.I64RemS,
            WasmOp.I64RemU, WasmOp.I64And, WasmOp.I64Or, WasmOp.I64Xor, WasmOp.I64Shl, WasmOp.I64ShrS, WasmOp.I64ShrU,
            WasmOp.I64Rotl, WasmOp.I64Rotr);
        Add(i64, i32, 2, WasmOp.I64Eq, WasmOp.I64Ne, WasmOp.I64LtS, WasmOp.I64LtU, WasmOp.I64GtS, WasmOp.I64GtU,
            WasmOp.I64LeS, WasmOp.I64LeU, WasmOp.I64GeS, WasmOp.I64GeU);
        Add(i64, i32, 1, WasmOp.I64Eqz);
        Add(i64, i64, 1, WasmOp.I64Clz, WasmOp.I64Ctz, WasmOp.I64Popcnt);

        Add(f32, f32, 2, WasmOp.F32Add, WasmOp.F32Sub, WasmOp.F32Mul, WasmOp.F32Div, WasmOp.F32Min, WasmOp.F32Max,
            WasmOp.F32Copysign);
        Add(f32, i32, 2, WasmOp.F32Eq, WasmOp.F32Ne, WasmOp.F32Lt, WasmOp.F32Gt, WasmOp.F32Le, WasmOp.F32Ge);
        Add(f32, f32, 1, WasmOp.F32Abs, WasmOp.F32Neg, WasmOp.F32Ceil, WasmOp.F32Floor, WasmOp.F32Trunc,
            WasmOp.F32Nearest, WasmOp.F32Sqrt);

        Add(f64, f64, 2, WasmOp.F64Add, WasmOp.F64Sub, WasmOp.F64Mul, WasmOp.F64Div, WasmOp.F64Min, WasmOp.F64Max,
            WasmOp.F64Copysign);
        Add(f64, i32, 2, WasmOp.F64Eq, WasmOp.F64Ne, WasmOp.F64Lt, WasmOp.F64Gt, WasmOp.F64Le, WasmOp.F64Ge);
        Add(f64, f64, 1, WasmOp.F64Abs, WasmOp.F64Neg, WasmOp.F64Ceil, WasmOp.F64Floor, WasmOp.F64Trunc,
            WasmOp.F64Nearest, WasmOp.F64Sqrt);

        // Conversions that cannot trap. Float to int is left to host calls, which saturate instead.
        Add(i32, f32, 1, WasmOp.F32ConvertI32S);
        Add(i64, f32, 1, WasmOp.F32ConvertI64S);
        Add(f64, f32, 1, WasmOp.F32DemoteF64);
        Add(i64, i32, 1, WasmOp.I32WrapI64);
        Add(i32, f32, 1, WasmOp.F32ConvertI32U);
        Add(i64, f32, 1, WasmOp.F32ConvertI64U);
        Add(i32, f64, 1, WasmOp.F64ConvertI32S, WasmOp.F64ConvertI32U);
        Add(i64, f64, 1, WasmOp.F64ConvertI64S, WasmOp.F64ConvertI64U);
        Add(f32, f64, 1, WasmOp.F64PromoteF32);
        Add(i32, i64, 1, WasmOp.I64ExtendI32S, WasmOp.I64ExtendI32U);

        return ops;
    }
}
