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

    public bool Undo() => Apply(_beforeNode, _beforePort);

    public bool Redo() => Apply(_afterNode, _afterPort);

    public void OnEvicted()
    {
        if (_joined == null)
            return;
        foreach (var merge in _joined)
            merge.OnEvicted();
    }

    private bool Apply(ScriptGraphNode? other, string otherPort)
    {
        if (_node.IsDestroyed)
            return false;
        try
        {
            if (other == null || other.IsDestroyed)
            {
                if (_exit)
                    _node.DisconnectExit(_port);
                else
                    _node.DisconnectInput(_port);
                return true;
            }
            ScriptMergeUndo? merge = null;
            if (!ReferenceEquals(other.Space, _node.Space)
                && !ScriptGraphEditing.TryJoin(_exit ? other : _node, _exit ? _node : other, out merge, out _))
                return false;
            bool joined = false;
            try
            {
                joined = _exit ? _node.ConnectExit(_port, other, otherPort, out _) : _node.ConnectInput(_port, other, otherPort, out _);
            }
            finally
            {
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
