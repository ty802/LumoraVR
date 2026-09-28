// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// A node that turns what an output gives into what an input takes, offered when a wire's two ends do not
// match: its type and overload, what its card is called, and the ports the two wires meet it at.
public readonly record struct ScriptConversion(string NodeType, string? Signature, string Title, string Input, string Output);
