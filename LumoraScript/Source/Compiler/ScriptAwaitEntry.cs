// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// One await node: the exits its answers run and, for each, the export that runs it with the payload lanes.
public sealed record ScriptAwaitEntry(string NodeId, IReadOnlyList<string> Exits, IReadOnlyList<string> Exports, Type PayloadType,
    NodeOverload Overload);
