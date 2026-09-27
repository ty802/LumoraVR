// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public enum GraphValueKind : byte
{
    Null,
    Bool,
    Number,
    String,

    // {"binding": n}: the object bound in the host's slot n.
    Binding,

    // A short list of numbers, the constant form of a vector.
    Numbers
}
