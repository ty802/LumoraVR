// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;

namespace Lumora.Script;

// A function's ports as a node's "sig" spells them: what it takes, a bar, what it gives back, each a comma
// list of name:type, such as "a:int,b:float3|sum:int,ok:bool". Either side may be empty, so "|" takes and
// gives nothing; text with no bar at all is only what it takes. A Function, its Returns and every node that
// calls it carry the same signature, so their ports always agree.
//
// Names are plain identifiers, never two alike across both sides, and never one the nodes keep for their
// own: "name" and "fn" are settings, and a call from another script has a "target" port, a "function"
// setting and a "reason" output beside the ports it carries. -xlinka
public sealed class FunctionSignature
{
    public const char Bar = '|';

    public static readonly FunctionSignature Empty = new(new PortList(Array.Empty<NodePort>()), new PortList(Array.Empty<NodePort>()));

    private static readonly string[] Reserved = { "name", "fn", "target", "function", "reason" };

    // What a call passes in, in order: the arguments a Function's body reads.
    public PortList Inputs { get; }

    // What a call gets back, in order: what a Return hands back.
    public PortList Outputs { get; }

    // The one spelling of these ports: trimmed, types by their own names, the bar always there.
    public string Canonical { get; }

    private FunctionSignature(PortList inputs, PortList outputs)
    {
        Inputs = inputs;
        Outputs = outputs;
        Canonical = Spell(inputs) + Bar + Spell(outputs);
    }

    public static bool TryParse(string? text, out FunctionSignature? signature, out string? error)
    {
        signature = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            signature = Empty;
            return true;
        }

        int bar = text.IndexOf(Bar);
        if (bar >= 0 && text.IndexOf(Bar, bar + 1) >= 0)
        {
            error = "ports have one '|', between what goes in and what comes out";
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!TryPorts(bar < 0 ? text : text[..bar], names, out var inputs, out error)
            || !TryPorts(bar < 0 ? string.Empty : text[(bar + 1)..], names, out var outputs, out error))
            return false;

        var parsed = new FunctionSignature(inputs!, outputs!);
        if (parsed.Canonical.Length > GraphLimits.Default.MaxSignatureLength)
        {
            error = $"ports are longer than {GraphLimits.Default.MaxSignatureLength} characters";
            return false;
        }
        signature = parsed;
        return true;
    }

    // A Function, a Return, a Call Function or an Evaluate Function: the nodes whose ports are their signature.
    public static bool IsFunctionNode(NodeKind kind)
        => kind is NodeKind.Function or NodeKind.Return or NodeKind.CallFunction or NodeKind.EvaluateFunction;

    // A Return, a Call Function or an Evaluate Function: the nodes that point at a Function in "fn" and carry
    // its ports.
    public static bool FollowsFunction(NodeKind kind)
        => kind is NodeKind.Return or NodeKind.CallFunction or NodeKind.EvaluateFunction;

    public static bool IsReserved(string name) => Array.IndexOf(Reserved, name) >= 0;

    public override string ToString() => Canonical;

    private static bool TryPorts(string text, HashSet<string> names, out PortList? ports, out string? error)
    {
        ports = null;
        error = null;
        var list = new List<NodePort>();
        text = text.Trim();
        if (text.Length > 0)
        {
            var entries = ScriptTypes.SplitTopLevel(text);
            if (entries == null)
            {
                error = $"the brackets in '{text}' do not pair up";
                return false;
            }
            foreach (var entry in entries)
            {
                if (entry.Length == 0)
                {
                    error = "a port is missing between two commas";
                    return false;
                }
                int colon = entry.IndexOf(':');
                if (colon < 0)
                {
                    error = $"'{entry}' needs a type, such as {entry}:float";
                    return false;
                }
                var name = entry[..colon].Trim();
                var typeName = entry[(colon + 1)..].Trim();
                if (!NodePort.IsValidName(name))
                {
                    error = $"'{name}' is not a port name: a letter, then letters, digits or _";
                    return false;
                }
                if (IsReserved(name))
                {
                    error = $"'{name}' is kept for a node's own port or setting, so no port can be called that";
                    return false;
                }
                if (!names.Add(name))
                {
                    error = $"two ports are called '{name}'";
                    return false;
                }
                if (!ScriptTypes.TryGet(typeName, out var type))
                {
                    error = typeName.Length == 0 ? $"'{name}' needs a type after the colon" : $"'{typeName}' is not a script type";
                    return false;
                }
                list.Add(new NodePort(name, type, null, hasDefault: false));
            }
        }
        ports = new PortList(list);
        return true;
    }

    private static string Spell(PortList ports)
    {
        var text = new StringBuilder();
        for (int i = 0; i < ports.Count; i++)
        {
            if (i > 0)
                text.Append(',');
            text.Append(ports[i].Name).Append(':').Append(ports[i].Type.Name);
        }
        return text.ToString();
    }
}
