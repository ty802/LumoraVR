// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Variables;

// Outcome of a write through a VariableScope. Every member has exactly one cause, so a caller can
// tell "nobody declared this" apart from "somebody declared it under another type" apart from "it
// exists but nothing will accept the write".
public enum VariableWriteResult
{
    Success,

    // No scope resolved, or the scope has nothing readable under that name and type.
    NotFound,

    // A variable of that NAME exists in the scope, but under a different value type.
    TypeMismatch,

    // The variable exists and is readable, but no registered variable will take a write (every
    // backing field is read-only or already driven).
    NotWritable,

    // The path or the value itself was rejected before any write was attempted.
    Invalid,
}
