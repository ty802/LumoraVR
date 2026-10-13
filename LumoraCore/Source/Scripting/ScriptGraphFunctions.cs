// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// A Function's Returns and calls carry its ports. Pointing one at a Function copies the Function's ports onto
// it, and new ports typed on a Function go to every node of its graph pointing at it, in the same undo step.
// Wires and values on ports that are gone go with them, so the graph never holds a wire to a port nothing has.
//
// The steps are kept in two runs, everything let go of first and every new signature after, so undo puts every
// signature back before it puts back a single wire, and each wire finds both its ends as they were. -xlinka
public static partial class ScriptGraphEditing
{
    // Points a Return, a Call Function or an Evaluate Function at a Function and gives it the Function's ports,
    // as one undo step.
    private static bool PointFunction(ScriptGraphNode user, ScriptGraphNode function, out string? error)
    {
        error = null;
        if (function.Overload is not { Definition.Kind: NodeKind.Function } shape)
        {
            error = $"a {user.Title} points at a Function";
            return false;
        }
        var entry = user.FindValue("fn");
        if (!ReferenceEquals(user.Space, function.Space)
            && !(entry != null ? entry.Reference.IsWriteAllowed() && entry.Json.IsWriteAllowed() : user.Values.IsWriteAllowed()))
        {
            error = "you may not change that node";
            return false;
        }
        if (!TryJoin(user, function, out var merge, out error))
            return false;
        var records = new List<IUndoBatch>();
        if (merge != null)
            records.Add(merge);
        var before = entry?.Reference.Value;
        try
        {
            user.SetReference("fn", function);
        }
        catch (UnauthorizedAccessException)
        {
            merge?.Undo();
            error = "you may not change that node";
            return false;
        }
        if (user.FindValue("fn") is not { } set || !ReferenceEquals(set.Reference.Target, function))
        {
            merge?.Undo();
            error = "you may not change that node";
            return false;
        }
        records.Add(new FieldEditUndoBatch(set.Reference, before ?? default(RefID), set.Reference.Value, ValueStep));

        var edits = new List<IUndoBatch>();
        if (!TakePorts(user, shape.Signature, records, edits))
        {
            for (int i = records.Count - 1; i >= 0; i--)
                records[i].Undo();
            error = "you may not change that node";
            return false;
        }
        records.AddRange(edits);
        Record(user.World!, ValueStep, records);
        return true;
    }

    // New ports typed on a Function, taken to every Return and call in its graph that points at it, all one undo
    // step. A node there whoever typed them may not change keeps its old ports, and its check says it is out of
    // date with the Function.
    private static bool SetFunctionPorts(ScriptGraphNode function, string signature, out string? error)
    {
        error = null;
        var drops = new List<IUndoBatch>();
        var edits = new List<IUndoBatch>();
        if (!TakePorts(function, signature, drops, edits))
        {
            error = "you may not change that node";
            return false;
        }
        foreach (var user in FollowersOf(function))
            TakePorts(user, signature, drops, edits);
        drops.AddRange(edits);
        if (drops.Count > 0)
            Record(function.World!, ValueStep, drops);
        return true;
    }

    // The Returns and calls in a Function's graph that point at it.
    private static List<ScriptGraphNode> FollowersOf(ScriptGraphNode function)
    {
        var found = new List<ScriptGraphNode>();
        if (function?.Space is not { } space)
            return found;
        foreach (var node in space.Nodes)
        {
            if (ReferenceEquals(node, function) || node.Overload is not { } overload || !FunctionSignature.FollowsFunction(overload.Definition.Kind))
                continue;
            if (ReferenceEquals(node.FindValue("fn")?.Reference.Target, function))
                found.Add(node);
        }
        return found;
    }

    // A function node given a signature: what it held on ports it will no longer have let go of into drops, then
    // the signature itself written, its step into edits. False, changing nothing, when the signature does not
    // read or whoever acts may not change the node.
    private static bool TakePorts(ScriptGraphNode node, string signature, List<IUndoBatch> drops, List<IUndoBatch> edits)
    {
        if (node == null || node.IsDestroyed)
            return false;
        var registry = node.Language.Registry;
        if (!registry.TryGetDefinition(node.NodeType.Value ?? string.Empty, out var definition)
            || !registry.TryResolve(definition, signature, out var next, out _))
            return false;
        string current = node.Signature.Value ?? string.Empty;
        // The same ports spelled another way are no change.
        bool changes = (node.Overload?.Signature ?? current) != next.Signature;
        if (changes && !node.Signature.IsWriteAllowed())
            return false;
        DropStalePorts(node, next, drops);
        if (!changes)
            return true;
        try
        {
            node.Signature.Value = next.Signature;
            FitNodeBody(node);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        if (node.Signature.Value != next.Signature)
            return false;
        edits.Add(new FieldEditUndoBatch(node.Signature, current, next.Signature, ValueStep));
        return true;
    }

    // Whatever a node holds on ports the shape it is taking does not have: wires into its inputs, wires from its
    // outputs into other nodes of its graph, and values set on its inputs, each blanked the way unwiring blanks
    // it, with its step. The node's own setting stays. A wire or value whoever acts may not change stays too.
    private static void DropStalePorts(ScriptGraphNode node, NodeOverload next, List<IUndoBatch> drops)
    {
        var inputs = new List<(string Port, ScriptGraphNode From, string FromPort)>();
        foreach (var wire in node.Inputs)
        {
            string port = wire.Port.Value ?? string.Empty;
            if (wire.Node.Target is { } from && next.InputIndex(port) < 0)
                inputs.Add((port, from, wire.NodePort.Value ?? string.Empty));
        }
        foreach (var (port, from, fromPort) in inputs)
            Unwired(node, port, from, fromPort, drops);

        if (node.Space is { } space)
        {
            var readers = new List<(ScriptGraphNode Reader, string Port, string FromPort)>();
            foreach (var other in space.Nodes)
            {
                if (ReferenceEquals(other, node))
                    continue;
                foreach (var wire in other.Inputs)
                {
                    string output = wire.NodePort.Value ?? string.Empty;
                    if (ReferenceEquals(wire.Node.Target, node) && next.OutputIndex(output) < 0)
                        readers.Add((other, wire.Port.Value ?? string.Empty, output));
                }
            }
            foreach (var (reader, port, output) in readers)
                Unwired(reader, port, node, output, drops);
        }

        foreach (var value in node.Values)
        {
            string port = value.Port.Value ?? string.Empty;
            if (FunctionSignature.IsReserved(port) || next.InputIndex(port) >= 0)
                continue;
            try
            {
                if (!string.IsNullOrEmpty(value.Json.Value))
                {
                    string json = value.Json.Value;
                    value.Json.Value = string.Empty;
                    if (string.IsNullOrEmpty(value.Json.Value))
                        drops.Add(new FieldEditUndoBatch(value.Json, json, string.Empty, ValueStep));
                }
                if (!value.Reference.Value.IsNull)
                {
                    var bound = value.Reference.Value;
                    value.Reference.Target = null!;
                    if (value.Reference.Value.IsNull)
                        drops.Add(new FieldEditUndoBatch(value.Reference, bound, value.Reference.Value, ValueStep));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Unwired(ScriptGraphNode reader, string port, ScriptGraphNode from, string fromPort, List<IUndoBatch> drops)
    {
        try
        {
            reader.DisconnectInput(port);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        if (reader.FindInput(port)?.Node.Target == null)
            drops.Add(new ScriptWireUndo(reader, false, port, from, fromPort, null, string.Empty));
    }
}
