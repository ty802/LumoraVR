// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Lumora.Script;

// graph.json: the source of truth for a script. Node instances, wires, positions, constants and
// binding slots; never a RefID and never compiled code. Each peer compiles its own module from this.
public sealed class ScriptGraph
{
    public const string FormatName = "loom.graph/1";

    public List<GraphBinding> Bindings { get; } = new();
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphWire> Flow { get; } = new();
    public List<GraphWire> Data { get; } = new();

    public static ScriptGraph Parse(string json, GraphLimits? limits = null)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return GraphReader.Read(bytes, limits ?? GraphLimits.Default);
    }

    public static ScriptGraph Parse(ReadOnlySpan<byte> utf8, GraphLimits? limits = null)
        => GraphReader.Read(utf8, limits ?? GraphLimits.Default);

    public byte[] ToUtf8Json() => GraphWriter.Write(this);

    public string ToJson() => Encoding.UTF8.GetString(ToUtf8Json());

    // SHA-256 of the canonical form: the same graph always hashes the same, however it was spelled when
    // it arrived, so a world full of copies of one object compiles it once.
    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(ToUtf8Json())).ToLowerInvariant();

    public GraphNode? FindNode(string id)
    {
        foreach (var node in Nodes)
        {
            if (node.Id == id)
                return node;
        }
        return null;
    }
}
