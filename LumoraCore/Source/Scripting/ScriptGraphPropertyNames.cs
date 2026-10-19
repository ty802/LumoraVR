// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Components.Scripting;
using Lumora.Script;
using Lumora.Shading;

namespace Lumora.Core.Scripting;

// A material shows one property per name, so nodes coming into a shader graph never bring a name the graph
// already uses: one that would takes the next free one, Image 2 after Image, the way a node placed in the
// graph is named. Two names clash when they make the same property name, Glow and glow alike. -xlinka
public static partial class ScriptGraphEditing
{
    // Room left after a root for the number counted on to it, inside the longest property name.
    private const int PropertyCountRoom = 8;

    // Renames each incoming node whose property name another node of the graph already has, in the order they
    // come. The renames come back as steps for the caller's undo step; nothing changes outside a shader graph.
    public static List<IUndoBatch> EnsureUniquePropertyNames(ScriptGraphSpace space, IEnumerable<ScriptGraphNode> incoming)
    {
        var renames = new List<IUndoBatch>();
        if (space == null || space.IsDestroyed || space.Kind.Value != GraphKind.Shader || incoming == null)
            return renames;
        var coming = new List<ScriptGraphNode>();
        var seen = new HashSet<ScriptGraphNode>();
        foreach (var node in incoming)
        {
            if (node is { IsDestroyed: false } && seen.Add(node))
                coming.Add(node);
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in space.Nodes)
        {
            if (!seen.Contains(node) && NodePropertyKey(NodePropertyName(node)) is { } key)
                taken.Add(key);
        }

        foreach (var node in coming)
        {
            var text = NodePropertyName(node);
            if (NodePropertyKey(text) is not { } key || taken.Add(key))
                continue;
            string root = PropertyNameRoot(text!);
            string name = root;
            for (int i = 2; i < 10000 && NodePropertyKey(name) is { } next && taken.Contains(next); i++)
                name = $"{root} {i}";
            string before = node.FindValue("name")?.Json.Value ?? string.Empty;
            try
            {
                node.SetValue("name", GraphValue.Of(name));
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            if (node.FindValue("name") is not { } entry || (entry.Json.Value ?? string.Empty) == before)
                continue;
            renames.Add(new FieldEditUndoBatch(entry.Json, before, entry.Json.Value, ValueStep));
            if (NodePropertyKey(name) is { } made)
                taken.Add(made);
        }
        return renames;
    }

    // The name a node gives its material property: its own, or its kind's default. Null for a node that takes
    // no name.
    private static string? NodePropertyName(ScriptGraphNode node)
    {
        if (node.Overload is not { } overload || overload.InputIndex("name") is not (int index and >= 0)
            || overload.Inputs[index].Type != ScriptTypes.String)
            return null;
        string? text = overload.Inputs[index].Default as string;
        if (node.FindValue("name")?.Json.Value is { Length: > 0 } json)
        {
            try
            {
                text = GraphValue.ParseJson(json).String;
            }
            catch (GraphFormatException)
            {
            }
        }
        return text;
    }

    // The property a name makes, or null for one that makes none; such a node is named after its id instead,
    // which nothing else shares.
    private static string? NodePropertyKey(string? text)
        => string.IsNullOrEmpty(text) || GlslNames.FromText(text, string.Empty) is not { Length: > 0 } key ? null : key;

    // What a renamed node counts on from: Image 2 goes on as Image 3, not Image 2 2.
    private static string PropertyNameRoot(string text)
    {
        text = text.Trim();
        int end = text.Length;
        while (end > 0 && char.IsAsciiDigit(text[end - 1]))
            end--;
        if (end < text.Length && end > 1 && text[end - 1] == ' ')
            text = text[..(end - 1)].TrimEnd();
        return text.Length > GlslNames.MaxLength - PropertyCountRoom ? text[..(GlslNames.MaxLength - PropertyCountRoom)].TrimEnd() : text;
    }
}
