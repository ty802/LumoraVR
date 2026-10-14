// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Inlines an overload as a short run of WebAssembly that a static method of the node class writes, for work
// one instruction per lane cannot say: a length, a product of two rotations, a vector scaled by a number.
// The named method takes a WasmBodyWriter. The C# body stays the oracle a test compares the run against, so
// the run does the same sums in the same order and comes out bit for bit the same.
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class WasmBodyAttribute : Attribute
{
    public string Writer { get; }

    public WasmBodyAttribute(string writer)
    {
        Writer = writer;
    }
}
