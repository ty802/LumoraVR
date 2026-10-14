// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Wasm;

namespace Lumora.Script;

// What a [WasmBody] writer has to work with: the node's input lanes to push, its output lanes to fill,
// constants, scratch locals and the plain numeric instructions an inlined overload may use. No memory, no
// control flow, no calls; select is the one choice it can make. -xlinka
public sealed class WasmBodyWriter
{
    private readonly WasmFunctionBuilder _f;
    private readonly Action<int, int> _load;
    private readonly int[][] _outputs;

    internal WasmBodyWriter(WasmFunctionBuilder f, Action<int, int> load, int[][] outputs)
    {
        _f = f;
        _load = load;
        _outputs = outputs;
    }

    // Pushes one lane of an input.
    public WasmBodyWriter In(int input, int lane)
    {
        _load(input, lane);
        return this;
    }

    // Pops into one lane of an output.
    public WasmBodyWriter Out(int output, int lane)
    {
        _f.LocalSet(_outputs[output][lane]);
        return this;
    }

    public WasmBodyWriter Op(byte opcode)
    {
        if (opcode != WasmOp.Select && !InlineOps.TryGet(opcode, out _))
            throw new InvalidOperationException($"opcode 0x{opcode:X2} cannot go in an inlined body");
        _f.Op(opcode);
        return this;
    }

    public WasmBodyWriter F32(float value)
    {
        _f.F32Const(value);
        return this;
    }

    public WasmBodyWriter I32(int value)
    {
        _f.I32Const(value);
        return this;
    }

    public int Temp(WasmValueType type) => _f.AddLocal(type);

    public WasmBodyWriter Get(int temp)
    {
        _f.LocalGet(temp);
        return this;
    }

    public WasmBodyWriter Set(int temp)
    {
        _f.LocalSet(temp);
        return this;
    }

    public WasmBodyWriter Tee(int temp)
    {
        _f.LocalTee(temp);
        return this;
    }
}
