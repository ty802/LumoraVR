// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What the node tool makes for a field held up to it: a node that holds it, one that writes it on a pulse, one
// that reads it, or the field itself to wire anywhere one is taken.
public enum ScriptFieldNodeKind : byte
{
    Drive,
    Write,
    Read,
    Reference
}
