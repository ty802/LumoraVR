// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// A flow wire's run count: how many times flow has left NodeId by Exit, in an exported global the host
// reads to show flow moving along that wire. A relay's exit has no counter of its own: its count is the sum
// of Export and Also, the counters of the exits that run it.
public sealed record ScriptTraceEntry(string NodeId, string Exit, string Export)
{
    public IReadOnlyList<string> Also { get; init; } = Array.Empty<string>();
}
