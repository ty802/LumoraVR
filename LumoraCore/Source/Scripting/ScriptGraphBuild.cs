// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// An unpacked graph read into a document: the graph, the world objects its bindings stand for in order,
// which node each id came from, and what could not be written down, by node.
public sealed class ScriptGraphBuild
{
    public ScriptGraph Graph { get; }
    public IReadOnlyList<IWorldElement> Bindings { get; }
    public IReadOnlyDictionary<string, ScriptGraphNode> Nodes { get; }
    public IReadOnlyList<(ScriptGraphNode? Node, string Message)> Problems { get; }

    public ScriptGraphBuild(ScriptGraph graph, IReadOnlyList<IWorldElement> bindings, IReadOnlyDictionary<string, ScriptGraphNode> nodes,
        IReadOnlyList<(ScriptGraphNode? Node, string Message)> problems)
    {
        Graph = graph;
        Bindings = bindings;
        Nodes = nodes;
        Problems = problems;
    }
}
