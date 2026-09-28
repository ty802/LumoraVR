// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Packs a graph being edited into a new script of the packer's own, on a new slot beside the graph's script,
// or in the same place under the world root where the packer may not put things there. The slot comes back
// at once; the script appears on it once the graph has compiled on a worker and been stored, and runs as
// whoever packed it. A world script packs only a graph open on one of the world's own scripts and makes world
// content; a guest's packs only its author's own work and the world's, where the world lets people bring
// things in and their role holds Spawn, and what it makes is theirs, not saved. At most ten packs a minute for
// each author, the world's scripts between them. A Local script cannot.
[Node("Graph/Pack Graph", Capability.WorldStructure)]
public sealed class PackGraph : ActionNode
{
    [return: Port("slot")]
    public static Slot? Run(ScriptContext ctx, Component? graph) => ScriptGraphEditing.PackFor(ctx, graph as ScriptGraphSpace);
}
