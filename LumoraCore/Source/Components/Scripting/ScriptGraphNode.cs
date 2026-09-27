// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Scripting;
using Lumora.Script;

namespace Lumora.Core.Components.Scripting;

// One node of a graph being edited. The slot is the node and its local position is the node's place in
// the graph; this holds what the graph document would: the node type, the overload, the version,
// constants, and the wires this node owns. Only the editing form: a packed script has no node slots.
//
// NodeId is the node's name in the document. It stays put across pack and unpack, so a stored value
// keeps its state through a graph swap; a copy gets a fresh one.
//
// Clearing a value or a wire blanks its entry instead of removing it: a Builder may edit someone else's
// node but not delete from it, and unwiring is an edit. A blank entry reads as not there. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptGraphNode : Component
{
    public readonly Sync<string> NodeId = new();
    public readonly Sync<string> NodeType = new();
    public readonly Sync<string> Signature = new();
    public readonly Sync<int> Version = new();
    public readonly SyncList<ScriptGraphValue> Values = new();
    public readonly SyncList<ScriptGraphWire> Inputs = new();
    public readonly SyncList<ScriptGraphWire> Exits = new();

    // What a Display or Pulse shows, written by the host as the script runs. Not part of the graph: it
    // is in no hash and no check, and no one else may write it.
    public readonly Sync<string> Shown = new();

    // Written by the host the same way, never saved: how many times flow has left by each wired exit
    // ("next:12,true:3"), and why the script last faulted here, while that is recent.
    [NonPersistent]
    public readonly Sync<string> Traced = new();
    [NonPersistent]
    public readonly Sync<string> Fault = new();

    private string? _resolvedType;
    private string? _resolvedSignature;
    private int _resolvedVersion;
    private NodeRegistry? _resolvedRegistry;
    private NodeOverload? _overload;
    private string? _resolveError;

    public override void OnInit()
    {
        base.OnInit();
        Version.Value = 1;
        NodeId.Value = FreshId();
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Shown.MarkHostOnly();
        Traced.MarkHostOnly();
        Fault.MarkHostOnly();
    }

    // On the host, from the running script's Displays; a text that has not changed sends nothing.
    internal void Show(string text)
    {
        if (World?.IsAuthority != true || Shown.Value == text)
            return;
        using (World.DataModelPermissions?.EnterSystemBypass())
            Shown.Value = text;
    }

    internal void ShowTrace(string text) => HostWrite(Traced, text);

    internal void ShowFault(string text) => HostWrite(Fault, text);

    private void HostWrite(Sync<string> field, string text)
    {
        if (World?.IsAuthority != true || field.Value == text)
            return;
        using (World.DataModelPermissions?.EnterSystemBypass())
            field.Value = text;
    }

    public override void OnDuplicate()
    {
        base.OnDuplicate();
        NodeId.Value = FreshId();
    }

    public string FreshId() => "n" + ReferenceID.RawValue.ToString("x");

    // The graph this node belongs to. A hand holding it parks it under the hand until it is let go, and
    // it stays the graph's all the while.
    public ScriptGraphSpace? Space
    {
        get
        {
            if (Slot?.Parent?.GetComponent<ScriptGraphSpace>() is { } space)
                return space;
            return Slot?.GetComponent<Grabbable>() is { IsGrabbed: true } grab
                ? grab.LastParentRef.Target?.GetComponent<ScriptGraphSpace>()
                : null;
        }
    }

    // The kind of graph this node is in: its graph's, or a script's while it is in none.
    public GraphLanguage Language => Space?.Language ?? GraphLanguage.Script;

    // The ports this node has here, from its kind's registry. Null, with the reason, when this build does not
    // know the type or the overload.
    public NodeOverload? Resolve(out string? error)
    {
        string type = NodeType.Value ?? string.Empty;
        string signature = Signature.Value ?? string.Empty;
        int version = Version.Value;
        var registry = Language.Registry;
        if (type != _resolvedType || signature != _resolvedSignature || version != _resolvedVersion
            || !ReferenceEquals(registry, _resolvedRegistry))
        {
            _resolvedType = type;
            _resolvedSignature = signature;
            _resolvedVersion = version;
            _resolvedRegistry = registry;
            _overload = null;
            _resolveError = null;

            if (!registry.TryGetDefinition(type, out var definition))
                _resolveError = $"unknown node type '{type}'";
            else if (definition.Version != version)
                _resolveError = $"{definition.Name} v{version}, this build has v{definition.Version}";
            else if (registry.TryResolve(definition, signature.Length == 0 ? null : signature, out var overload, out var why))
                _overload = overload;
            else
                _resolveError = why;
        }
        error = _resolveError;
        return _overload;
    }

    public NodeOverload? Overload => Resolve(out _);

    // The last part of the node's path, which is what a card shows.
    public string Title
    {
        get
        {
            var path = Overload?.Definition.Path;
            if (string.IsNullOrEmpty(path))
                return NodeType.Value ?? string.Empty;
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path[(slash + 1)..] : path;
        }
    }

    public string Category
    {
        get
        {
            var path = Overload?.Definition.Path;
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            int slash = path.IndexOf('/');
            return slash >= 0 ? path[..slash] : path;
        }
    }

    // Everything a check reads off this node, folded into one number. Walked rather than trusted to change
    // notices, since an edit inside a list entry is what a graph edit mostly is.
    public int ContentHash()
    {
        var hash = new HashCode();
        hash.Add(NodeId.Value);
        hash.Add(NodeType.Value);
        hash.Add(Signature.Value);
        hash.Add(Version.Value);
        foreach (var value in Values)
        {
            hash.Add(value.Port.Value);
            hash.Add(value.Json.Value);
            hash.Add(value.Reference.Value.RawValue);
            hash.Add(value.Reference.Target is { IsDestroyed: false });
        }
        foreach (var wire in Inputs)
        {
            hash.Add(wire.Port.Value);
            hash.Add(wire.Node.Value.RawValue);
            hash.Add(wire.NodePort.Value);
        }
        foreach (var wire in Exits)
        {
            hash.Add(1);
            hash.Add(wire.Port.Value);
            hash.Add(wire.Node.Value.RawValue);
            hash.Add(wire.NodePort.Value);
        }
        return hash.ToHashCode();
    }

    public ScriptGraphValue? FindValue(string port)
    {
        foreach (var value in Values)
        {
            if (value.Port.Value == port)
                return value;
        }
        return null;
    }

    public void SetValue(string port, GraphValue value)
    {
        var entry = FindValue(port) ?? AddValue(port);
        entry.Reference.Target = null!;
        entry.Json.Value = value.ToJson();
    }

    // A world object bound to an input, or the Stored Value node a Write Value writes.
    public void SetReference(string port, IWorldElement? target)
    {
        var entry = FindValue(port) ?? AddValue(port);
        entry.Json.Value = string.Empty;
        entry.Reference.Target = target!;
    }

    public void ClearValue(string port)
    {
        foreach (var value in Values)
        {
            if (value.Port.Value != port)
                continue;
            if (!string.IsNullOrEmpty(value.Json.Value))
                value.Json.Value = string.Empty;
            if (!value.Reference.Value.IsNull)
                value.Reference.Target = null!;
        }
    }

    public ScriptGraphWire? FindInput(string port) => Find(Inputs, port);

    public ScriptGraphWire? FindExit(string exit) => Find(Exits, exit);

    // Wires an output of another node into one of this node's inputs, replacing whatever fed it. Refused,
    // with the reason, when the two do not fit.
    public bool ConnectInput(string input, ScriptGraphNode from, string output, out string? error)
    {
        error = CheckData(from, output, input);
        if (error != null)
            return false;
        var wire = FindInput(input) ?? AddWire(Inputs, input);
        wire.Node.Target = from;
        wire.NodePort.Value = output;
        return true;
    }

    // Runs another node after one of this node's exits, replacing whatever ran there.
    public bool ConnectExit(string exit, ScriptGraphNode to, string input, out string? error)
    {
        error = CheckFlow(exit, to, input);
        if (error != null)
            return false;
        var wire = FindExit(exit) ?? AddWire(Exits, exit);
        wire.Node.Target = to;
        wire.NodePort.Value = input;
        return true;
    }

    public void DisconnectInput(string input) => Remove(Inputs, input);

    public void DisconnectExit(string exit) => Remove(Exits, exit);

    // Why this node's input could not read another's output, or its exit not run another node, leaving aside
    // whether the two are in one graph yet; null when it could. What joins two groups asks this first.
    public string? WireProblem(bool exit, string port, ScriptGraphNode other, string otherPort)
        => exit ? CheckFlow(port, other, otherPort, anyGraph: true) : CheckData(other, otherPort, port, anyGraph: true);

    private string? CheckData(ScriptGraphNode from, string output, string input, bool anyGraph = false)
    {
        if (from == null || from.IsDestroyed)
            return "that node is gone";
        if (ReferenceEquals(from, this))
            return "a node cannot read its own output";
        if (GraphProblem(from, anyGraph) is { } apart)
            return apart;
        var consumer = Resolve(out var error);
        if (consumer == null)
            return error;
        var producer = from.Resolve(out error);
        if (producer == null)
            return error;
        int inputIndex = consumer.InputIndex(input);
        if (inputIndex < 0)
            return $"{Title} has no input '{input}'";
        int outputIndex = producer.OutputIndex(output);
        if (outputIndex < 0)
            return $"{from.Title} has no output '{output}'";
        var wanted = consumer.Inputs[inputIndex].Type;
        var given = producer.Outputs[outputIndex].Type;
        return Language.Accepts(wanted, given) ? null : $"'{input}' takes {wanted.Name}, '{output}' gives {given.Name}";
    }

    private string? CheckFlow(string exit, ScriptGraphNode to, string input, bool anyGraph = false)
    {
        if (to == null || to.IsDestroyed)
            return "that node is gone";
        if (GraphProblem(to, anyGraph) is { } apart)
            return apart;
        var from = Resolve(out var error);
        if (from == null)
            return error;
        var next = to.Resolve(out error);
        if (next == null)
            return error;
        if (!Contains(from.FlowOutputs, exit))
            return $"{Title} has no flow output '{exit}'";
        if (!Contains(next.FlowInputs, input))
            return $"{to.Title} has no flow input '{input}'";
        return null;
    }

    // A wire stays inside one graph; the editor puts two groups together before it wires across them.
    private string? GraphProblem(ScriptGraphNode other, bool anyGraph)
    {
        var space = Space;
        var otherSpace = other.Space;
        if (space == null || otherSpace == null)
            return anyGraph ? "that node is not in a graph" : "those nodes are in different graphs";
        return anyGraph || ReferenceEquals(space, otherSpace) ? null : "those nodes are in different graphs";
    }

    private ScriptGraphValue AddValue(string port)
    {
        var entry = Values.Add();
        entry.Port.Value = port;
        return entry;
    }

    private static ScriptGraphWire AddWire(SyncList<ScriptGraphWire> list, string port)
    {
        var wire = list.Add();
        wire.Port.Value = port;
        return wire;
    }

    private static ScriptGraphWire? Find(SyncList<ScriptGraphWire> list, string port)
    {
        foreach (var wire in list)
        {
            if (wire.Port.Value == port)
                return wire;
        }
        return null;
    }

    private static void Remove(SyncList<ScriptGraphWire> list, string port)
    {
        foreach (var wire in list)
        {
            if (wire.Port.Value != port || wire.Node.Value.IsNull)
                continue;
            wire.Node.Target = null!;
            wire.NodePort.Value = string.Empty;
        }
    }

    private static bool Contains(System.Collections.Generic.IReadOnlyList<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
