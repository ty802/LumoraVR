// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Inlines an overload as one WebAssembly instruction per lane instead of a call into its C# body. The
// body stays as the oracle a test compares the instruction against. Lanes, when given, has to agree
// with the lane count of the overload's types; it is a check, not a setting.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class WasmAttribute : Attribute
{
    public byte Opcode { get; }
    public int Lanes { get; init; }

    public WasmAttribute(byte opcode)
    {
        Opcode = opcode;
    }
}
