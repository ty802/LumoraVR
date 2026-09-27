// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a Once, a Toggle or a Cooldown remembers between runs, in an exported global, so a live graph swap
// can carry it to the same node of the same kind in the new graph.
public sealed record ScriptMemoryEntry(string NodeId, FlowShapeKind Kind, string Export);
