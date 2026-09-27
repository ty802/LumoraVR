// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A Display or Pulse node, read through the stored variable of the same node id. CountsRuns is a Pulse's
// run count rather than a shown value. Export is the function that works a Display out, for one that
// needs no flow; Captured, one a flow's call leaves behind. A Display with neither never has a value.
public sealed record ScriptDisplayEntry(string NodeId, bool CountsRuns, string? Export, bool Captured)
{
    public bool Refreshed => Export != null;
}
