// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Script;

namespace Lumora.Shading;

// Loom graph document in, Godot shader source out. Each machine compiles the graph it was sent, so no
// shader text ever travels: what reaches the GPU is only ever code this compiler wrote from its own
// nodes, and the sandbox checks it again anyway. Safe to call from any thread. -xlinka
public static class ShaderGraphCompiler
{
    public static ShaderProgram Compile(ScriptGraph graph) => Compile(graph, ShaderNodes.Registry);

    public static ShaderProgram Compile(ScriptGraph graph, NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new ShaderBuild(graph, registry).Compile();
    }

    // A shader showing one node's output on its own, for the preview on its card. Port null is the
    // node's first output.
    public static ShaderProgram CompilePreview(ScriptGraph graph, string nodeId, string? port = null)
        => CompilePreview(graph, ShaderNodes.Registry, nodeId, port);

    public static ShaderProgram CompilePreview(ScriptGraph graph, NodeRegistry registry, string nodeId, string? port = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new ShaderBuild(graph, registry).CompilePreview(nodeId, port);
    }
}
