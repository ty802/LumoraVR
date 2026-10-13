// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// One Function of the graph and the export it lowers to. The export takes the lanes of what the Function
// takes, then where in memory its continuation stack may start, 0 from outside the module; it gives back the
// lanes of what the Function gives. Pure: its body is one Return of values that need no flow, so Evaluate
// Function may use it. Async: its body holds a Delay, an await or a call of a Function that waits, so its
// export takes the word of a call begun for it last and gives nothing back, and what it gives reaches that
// call's ticket when it returns.
public sealed record ScriptFunctionEntry(string NodeId, string Name, string Export, FunctionSignature Signature, bool Pure, bool Async = false);
