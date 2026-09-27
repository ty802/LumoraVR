// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Script;

// A stored variable and the exported globals holding its lanes. A live graph swap carries a value over
// by name and type; the handle collector reads the handle-typed ones as roots.
public sealed record ScriptVariableEntry(string NodeId, string Name, ScriptType Type, IReadOnlyList<string> Exports);
