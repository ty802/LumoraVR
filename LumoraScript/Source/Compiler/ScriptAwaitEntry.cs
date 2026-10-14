// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// One await node: the exits its answers run and, for each, the export that runs it with the payload lanes, and
// after them the word of the waiting Function's call it was started in when it carries one. A call of a waiting
// Function is one too, its next the exit its answer runs.
public sealed record ScriptAwaitEntry(string NodeId, IReadOnlyList<string> Exits, IReadOnlyList<string> Exports, Type PayloadType,
    NodeOverload Overload, bool CarriesWord = false);
