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
    Await,

    // A value worked out from what the node remembers as well as its inputs, by a shape the compiler lowers.
    Memory,

    // Holds a field at a value worked out every frame, on every machine running the script.
    Drive,

    // A point on a wire, data or flow, that the compiler reads straight through and never lowers.
    Relay,

    // A function of the graph's own: the node its body starts from, the Return that hands back what it gives,
    // and the two ways to use it, a call in flow and a pure call in data. Their ports are the node's own
    // "sig" (FunctionSignature), not the class's.
    Function,
    Return,
    CallFunction,
    EvaluateFunction
}
