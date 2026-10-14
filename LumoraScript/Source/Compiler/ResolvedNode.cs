// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Script;

// A graph node bound to its overload, with every input's source and every flow exit's target filled
// in. The tag is the node's index, which is also its source map tag.
internal sealed class ResolvedNode
{
    public GraphNode Source { get; }
    public int Tag { get; }
    public NodeOverload Overload { get; }
    public DataSource?[] Inputs { get; }
    public ResolvedNode?[] Exits { get; }

    // Stored variables: the globals holding the lanes, the starting value and the swap name.
    public int[] Globals { get; set; } = Array.Empty<int>();
    public ConstantValue? Initial { get; set; }
    public string? VariableName { get; set; }

    // A variable write: the stored variable it writes.
    public ResolvedNode? Variable { get; set; }

    // A Return, Call Function or Evaluate Function: the Function it returns from or calls.
    public ResolvedNode? Function { get; set; }

    // A Function: the name it is called and exported by, the module function it lowers to, and, when its body
    // is one Return of values that need no flow, that Return, which is what lets it be evaluated.
    public string? FunctionName { get; set; }
    public int FunctionIndex { get; set; } = -1;
    public ResolvedNode? PureReturn { get; set; }

    public bool IsPure => PureReturn != null;

    // A Function whose body waits (a Delay, an await, or a call of a Function that waits), and the first such
    // thing found, for saying why. A Call Function of one is an async call: it waits for its answer like an
    // await, and its next runs in a call of its own once the Function returns.
    public bool IsAsync { get; set; }
    public string? WaitReason { get; set; }
    public bool IsAsyncCall { get; set; }

    // A waiting Function's imports: what its Return gives, and its arguments kept for after a wait. -1 for none.
    public int DoneImport { get; set; } = -1;
    public int KeepImport { get; set; } = -1;

    // A Delay, an await or an async call: the events and Functions whose flows reach it. Its continuation carries
    // the word of a waiting Function's call when any Function's does.
    public HashSet<ResolvedNode>? Owners { get; set; }
    public bool CarriesWord { get; set; }

    // Per flow exit, the global counting flow that leaves by it, or -1 for an exit with no wire.
    public int[] TraceGlobals { get; set; } = Array.Empty<int>();

    // An action's failed exit when something is wired there, else -1. Only then does the module ask whether the
    // call failed.
    public int FailedExit => Kind == NodeKind.Action && _failed >= 0 && Exits[_failed] != null ? _failed : -1;

    private readonly int _failed = -1;

    public int ImportIndex { get; set; } = -1;

    // A list For Each: the host calls it counts and reads the list with. A scope: the action nodes it opens
    // and closes with. And their import indexes.
    public NodeOverload[] Helpers { get; set; } = Array.Empty<NodeOverload>();
    public int[] HelperImports { get; set; } = Array.Empty<int>();

    // A delay: its number in the module's timer table.
    public int TimerIndex { get; set; } = -1;

    // An await: its number, which the module passes its Start so the ticket knows whose answer it carries.
    public int AwaitIndex { get; set; } = -1;

    // A sourced event: the binding naming the object it listens to, or -1 for wherever the script sits.
    public int SourceBinding { get; set; } = -1;

    // A switched drive's global holding whether it is on, or -1.
    public int SwitchGlobal { get; set; } = -1;

    // A holding drive's bindings that hear what it refuses, or -1.
    public int HeardBinding { get; set; } = -1;
    public int HeardCountBinding { get; set; } = -1;

    // A timed event: how many seconds apart the host raises it.
    public float IntervalSeconds { get; set; }

    public ResolvedNode(GraphNode source, int tag, NodeOverload overload)
    {
        Source = source;
        Tag = tag;
        Overload = overload;
        Inputs = new DataSource?[overload.Inputs.Count];
        Exits = new ResolvedNode?[overload.FlowOutputs.Count];
        for (int exit = 0; exit < overload.FlowOutputs.Count; exit++)
        {
            if (overload.FlowOutputs[exit] == ActionNode.FailedExit)
                _failed = exit;
        }
    }

    public string Id => Source.Id;

    public NodeKind Kind => Overload.Definition.Kind;

    // A node that runs its body again and again, the only way a flow may lead back to itself.
    public bool IsLoop => Overload.Shape?.Kind is FlowShapeKind.Repeat or FlowShapeKind.While or FlowShapeKind.Each
        or FlowShapeKind.Range or FlowShapeKind.ListEach;

    public bool IsDelay => Overload.Shape?.Kind == FlowShapeKind.Delay;

    public bool IsAwait => Kind == NodeKind.Await;

    // An exit flow leaves by in the call that reached the node, not in a later one: every exit but those
    // of a Delay and an async call, and an await's next.
    public bool RunsNow(int exit) => !IsDelay && !IsAsyncCall && (!IsAwait || exit == 0);

    public override string ToString() => $"{Id} ({Overload.ImportName})";
}
