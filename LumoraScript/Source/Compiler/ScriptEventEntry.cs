// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// One event node's entry point: the export the host invokes with the payload lanes, and for a sourced
// event the binding naming what it listens to (-1: wherever the script sits).
public sealed record ScriptEventEntry(string NodeId, string EventName, string Export, Type PayloadType, NodeOverload Overload,
    int SourceBinding = -1, float IntervalSeconds = 0f);
