// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// How a script value travels as WebAssembly values.
public enum ScriptTypeKind : byte
{
    Bool,
    Int,
    Long,
    Float,
    Double,

    // A struct of numbers, one lane per number in declaration order, nested structs flattened: float3,
    // floatQ, color, int2, bool4, a matrix by its columns, a box by its corners.
    Vector,

    // Something a script may hold and pass along but never look inside: an index into the instance's
    // handle table. Strings, slots, users, lists and fields all travel this way. -xlinka
    Handle,

    // A value whose lanes a codec states: one narrower than its lane (a byte, a char), one whose bits are
    // its own business (a date, a span of time, a guid), a quantity with units, or one of an enum's names.
    Coded
}
