// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Wasm;

public enum WasmTrapCode
{
    Unreachable,
    IntegerDivideByZero,
    IntegerOverflow,
    InvalidConversionToInteger,
    OutOfBoundsMemoryAccess,
    OutOfBoundsTableAccess,
    UndefinedElement,
    UninitializedElement,
    IndirectCallTypeMismatch,
    CallStackExhausted,
    OutOfFuel,
    ResourceLimit,
    HostTrap,
    OutOfTime,
}

public class WasmException : Exception
{
    public WasmException(string message) : base(message)
    {
    }
}

// The bytes are not a WebAssembly module at all.
public sealed class WasmFormatException : WasmException
{
    public WasmFormatException(string message) : base(message)
    {
    }
}

// Well formed, but breaks a typing or structural rule.
public sealed class WasmValidationException : WasmException
{
    public WasmValidationException(string message) : base(message)
    {
    }
}

// Valid, but its imports cannot be satisfied by what the linker offers.
public sealed class WasmLinkException : WasmException
{
    public WasmLinkException(string message) : base(message)
    {
    }
}

public sealed class WasmTrapException : WasmException
{
    public WasmTrapCode Code { get; }

    // Where the innermost WebAssembly frame was when it trapped: the module function index and the byte
    // offset of the instruction in the module. -1 when the trap happened outside any function body
    // (instantiation, a direct host call). A compiler's source map turns the offset back into its own
    // terms, a graph node for Loom.
    public int FunctionIndex { get; private set; } = -1;
    public int ByteOffset { get; private set; } = -1;
    public WasmModule? Module { get; private set; }

    public WasmTrapException(WasmTrapCode code, string? detail = null)
        : base(detail == null ? Describe(code) : Describe(code) + ": " + detail)
    {
        Code = code;
    }

    internal bool HasLocation => FunctionIndex >= 0;

    internal void SetLocation(WasmModule module, int functionIndex, int byteOffset)
    {
        Module = module;
        FunctionIndex = functionIndex;
        ByteOffset = byteOffset;
    }

    // What the call that trapped still had to spend when it stopped.
    public long FuelLeft { get; internal set; }

    public static string Describe(WasmTrapCode code) => code switch
    {
        WasmTrapCode.Unreachable => "unreachable",
        WasmTrapCode.IntegerDivideByZero => "integer divide by zero",
        WasmTrapCode.IntegerOverflow => "integer overflow",
        WasmTrapCode.InvalidConversionToInteger => "invalid conversion to integer",
        WasmTrapCode.OutOfBoundsMemoryAccess => "out of bounds memory access",
        WasmTrapCode.OutOfBoundsTableAccess => "out of bounds table access",
        WasmTrapCode.UndefinedElement => "undefined element",
        WasmTrapCode.UninitializedElement => "uninitialized element",
        WasmTrapCode.IndirectCallTypeMismatch => "indirect call type mismatch",
        WasmTrapCode.CallStackExhausted => "call stack exhausted",
        WasmTrapCode.OutOfFuel => "out of fuel",
        WasmTrapCode.ResourceLimit => "resource limit",
        WasmTrapCode.HostTrap => "host trap",
        WasmTrapCode.OutOfTime => "out of time",
        _ => "trap",
    };
}
