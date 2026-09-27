// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public enum NodeKind : byte
{
    Value,
    Read,
    Action,
    Event,
    Flow,

    // The compiler's own nodes: a stored variable and the write to one, a shown value, a run counter and
    // a value set on the card. They lower to globals or straight reads, never to host calls.
    StoredVariable,
    WriteVariable,
    Display,
    Pulse,
    Constant,

    // Starts work the host answers later; each answer runs one of its exits as a call of its own.
    Await
}
