// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Script;

// One piece of work an await node started, answered once, from any thread. A second answer, an answer
// with an exit the node does not have, or one after the script stopped is dropped. Cancelled trips when
// the script stops, so the work can stop with it.
public abstract class AwaitTicket
{
    private readonly ScriptAwaits _owner;
    private int _answered;

    private protected AwaitTicket(ScriptAwaits owner, string nodeId, CancellationToken cancelled)
    {
        _owner = owner;
        NodeId = nodeId;
        Cancelled = cancelled;
    }

    public string NodeId { get; }

    public CancellationToken Cancelled { get; }

    public bool IsAnswered => Volatile.Read(ref _answered) != 0;

    private protected bool Post(string exit, object payload)
    {
        if (Interlocked.Exchange(ref _answered, 1) != 0)
            return false;
        return _owner.Post(NodeId, exit, payload);
    }

    // The work is dropped without an answer: it no longer counts as out.
    public void Abandon()
    {
        if (Interlocked.Exchange(ref _answered, 1) == 0)
            _owner.Release();
    }
}

public sealed class AwaitTicket<TPayload> : AwaitTicket where TPayload : struct
{
    internal AwaitTicket(ScriptAwaits owner, string nodeId, CancellationToken cancelled) : base(owner, nodeId, cancelled)
    {
    }

    public bool Answer(string exit, in TPayload payload) => Post(exit, payload);
}
