// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;
using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// What the node editor asks of a kind of graph: which nodes there are, which wires fit, and how a graph is
// checked. Moving, wiring, cutting, snapping and undo are the same for every kind; what differs is here.
// -xlinka
public abstract class GraphLanguage
{
    public static GraphLanguage Script => ScriptGraphLanguage.Instance;

    public static GraphLanguage Shader => ShaderGraphLanguage.Instance;

    public static GraphLanguage For(GraphKind kind) => kind == GraphKind.Shader ? Shader : Script;

    public abstract GraphKind Kind { get; }

    // What a graph of this kind is called to whoever edits it.
    public abstract string Noun { get; }

    public abstract NodeRegistry Registry { get; }

    // Whether an input of type wanted may read an output of type given.
    public abstract bool Accepts(ScriptType wanted, ScriptType given);

    // Checks a graph on a worker: its problems and what it compiled to. Never faults.
    public abstract Task<GraphCheck> CheckAsync(ScriptGraph graph);
}
