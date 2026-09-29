// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a JSON value is.
public enum ScriptJsonKind : byte
{
    Null,
    Bool,
    Number,
    String,
    Array,
    Object
}
