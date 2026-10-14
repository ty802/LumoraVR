// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Localization;

namespace Lumora.Core.Scripting;

// One wire end before and after an edit: which node and port a node's input read from, or which node a
// node's exit ran next. Undo puts the old end back, or unwires when there was none.
//
// A wire put back between nodes that someone else's undo has since split into two groups joins the groups
// again first, and the group that empties stays parked until this step leaves the history. -xlinka
internal sealed class ScriptWireUndo : IUndoBatch
{
    private readonly ScriptGraphNode _node;
    private readonly bool _exit;
    private readonly string _port;
    private readonly ScriptGraphNode? _beforeNode;
    private readonly string _beforePort;
    private readonly ScriptGraphNode? _afterNode;
    private readonly string _afterPort;
    private string? _beforeSignature;
    private string? _afterSignature;
    private List<ScriptMergeUndo>? _joined;

    public ScriptWireUndo(ScriptGraphNode node, bool exit, string port, ScriptGraphNode? beforeNode, string beforePort,
        ScriptGraphNode? afterNode, string afterPort)
    {
        _node = node;
        _exit = exit;
        _port = port;
        _beforeNode = beforeNode;
        _beforePort = beforePort;
        _afterNode = afterNode;
        _afterPort = afterPort;
    }

    public static ScriptWireUndo Capture(ScriptGraphNode node, bool exit, string port, ScriptGraphNode? afterNode, string afterPort,
        ScriptGraphNode? beforeNode, string beforePort)
        => new(node, exit, port, beforeNode, beforePort, afterNode, afterPort);

    public string Description => ScriptGraphEditing.WireStep.Resolve();

    public LocaleText LocalizedDescription => ScriptGraphEditing.WireStep;

    public void CaptureSignatures(string before, string after)
    {
        if (before == after)
            return;
        _beforeSignature = before;
        _afterSignature = after;
    }

    public bool Undo() => Apply(_beforeNode, _beforePort, _beforeSignature);

    public bool Redo() => Apply(_afterNode, _afterPort, _afterSignature);

    public void OnEvicted()
    {
        if (_joined == null)
            return;
        foreach (var merge in _joined)
            merge.OnEvicted();
    }

    private bool Apply(ScriptGraphNode? other, string otherPort, string? signature)
    {
        if (_node.IsDestroyed)
            return false;
        try
        {
            var entry = _exit ? _node.FindExit(_port) : _node.FindInput(_port);
            if (signature != null && !ScriptGraphOverloads.CanWrite(_node.Signature)
                || (entry != null ? !ScriptGraphOverloads.CanWrite(entry.Node) || !ScriptGraphOverloads.CanWrite(entry.NodePort)
                    : !ScriptGraphOverloads.CanWrite(_exit ? _node.Exits : _node.Inputs)))
                return false;
            ScriptMergeUndo? merge = null;
            if (other is { IsDestroyed: false } && !ReferenceEquals(other.Space, _node.Space)
                && !ScriptGraphEditing.TryJoin(_exit ? other : _node, _exit ? _node : other, out merge, out _))
                return false;
            string beforeSignature = _node.Signature.Value ?? string.Empty;
            bool joined = false;
            try
            {
                // Restoring the old wire requires its old type first; restoring a wire before a separate
                // signature undo would reject it as incompatible.
                if (signature != null)
                {
                    _node.Signature.Value = signature;
                    if (_node.Signature.Value != signature)
                        return false;
                }
                if (other == null || other.IsDestroyed)
                {
                    if (_exit)
                        _node.DisconnectExit(_port);
                    else
                        _node.DisconnectInput(_port);
                    joined = true;
                }
                else
                    joined = _exit ? _node.ConnectExit(_port, other, otherPort, out _) : _node.ConnectInput(_port, other, otherPort, out _);
            }
            finally
            {
                if (!joined && _node.Signature.Value != beforeSignature)
                    _node.Signature.Value = beforeSignature;
                if (_node.Signature.Value != beforeSignature)
                    ScriptGraphEditing.FitNodeBody(_node);
                if (merge != null && joined)
                    (_joined ??= new List<ScriptMergeUndo>()).Add(merge);
                else
                    merge?.Undo();
            }
            return joined;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
