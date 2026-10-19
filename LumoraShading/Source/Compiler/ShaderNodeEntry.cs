// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Lumora.Script;

namespace Lumora.Shading;

// One graph node as the compiler resolved it: its overload, its behaviour and what feeds its inputs.
internal sealed class ShaderNodeEntry
{
    public GraphNode Node { get; }
    public int Index { get; }
    public NodeOverload Overload { get; }
    public ShaderNode Behaviour { get; }
    public Dictionary<string, (string Node, string Port)> Wires { get; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ShaderPortInfo> Ports { get; }

    public string Id => Node.Id;
    public string Prefix => $"n{Index}";
    public string Title => Overload.Definition.Name;

    public ShaderNodeEntry(GraphNode node, int index, NodeOverload overload, ShaderNode behaviour)
    {
        Node = node;
        Index = index;
        Overload = overload;
        Behaviour = behaviour;

        Ports = overload.Method != null ? PortsOf(overload.Method) : NoPorts;
    }

    private static readonly IReadOnlyDictionary<string, ShaderPortInfo> NoPorts = new Dictionary<string, ShaderPortInfo>();
    private static readonly ConcurrentDictionary<MethodInfo, IReadOnlyDictionary<string, ShaderPortInfo>> PortCache = new();

    // A node method's ports read once, not on every compile: a gallery load compiles each graph and then a
    // preview per node, every one of them walking the same methods for the same parameters and attributes,
    // about 25 MB of reflection garbage a load. -xlinka
    private static IReadOnlyDictionary<string, ShaderPortInfo> PortsOf(MethodInfo method) => PortCache.GetOrAdd(method, static m =>
    {
        // Port names are the parameter names with the first letter lowered, or what [Port] says.
        var ports = new Dictionary<string, ShaderPortInfo>(StringComparer.Ordinal);
        foreach (var parameter in m.GetParameters())
        {
            if (parameter.IsOut)
                continue;
            var name = parameter.GetCustomAttribute<PortAttribute>()?.Name ?? parameter.Name;
            if (!string.IsNullOrEmpty(name) && NodePort.IsValidName(name))
            {
                ports[NodePort.NormalizeName(name)] = new ShaderPortInfo(
                    parameter.GetCustomAttribute<VertexStageAttribute>() != null,
                    parameter.GetCustomAttribute<GlslDefaultAttribute>());
            }
        }
        return ports;
    });

    public NodePort? Input(string port)
    {
        foreach (var input in Overload.Inputs)
        {
            if (input.Name == port)
                return input;
        }
        return null;
    }

    public NodePort? Output(string port)
    {
        foreach (var output in Overload.Outputs)
        {
            if (output.Name == port)
                return output;
        }
        return null;
    }
}
