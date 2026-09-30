// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
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
    public Dictionary<string, ParameterInfo> Parameters { get; } = new(StringComparer.Ordinal);

    public string Id => Node.Id;
    public string Prefix => $"n{Index}";
    public string Title => Overload.Definition.Name;

    public ShaderNodeEntry(GraphNode node, int index, NodeOverload overload, ShaderNode behaviour)
    {
        Node = node;
        Index = index;
        Overload = overload;
        Behaviour = behaviour;

        // Port names are the parameter names with the first letter lowered, or what [Port] says.
        if (overload.Method != null)
        {
            foreach (var parameter in overload.Method.GetParameters())
            {
                if (parameter.IsOut)
                    continue;
                var name = parameter.GetCustomAttribute<PortAttribute>()?.Name ?? parameter.Name;
                if (!string.IsNullOrEmpty(name) && NodePort.IsValidName(name))
                    Parameters[NodePort.NormalizeName(name)] = parameter;
            }
        }
    }

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
