// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A flow wire's run count: how many times flow has left NodeId by Exit, in an exported global the host
// reads to show flow moving along that wire.
public sealed record ScriptTraceEntry(string NodeId, string Exit, string Export);
