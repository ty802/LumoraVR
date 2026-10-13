// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;

namespace Lumora.Script;

// A call a node is waiting on, answered once, from any thread, with values rather than a payload struct: what
// the Function gave back, in the order its outputs are declared, or why it could not run. A node with a
// failed exit hears the reason there; one without lets the call go. -xlinka
public sealed class CallTicket : AwaitTicket
{
    public const string ReturnedExit = "returned";
    public const string FailedExit = "failed";
    public const string ReasonPort = "reason";

    private readonly NodeOverload _overload;

    internal CallTicket(ScriptAwaits owner, string nodeId, CancellationToken cancelled, NodeOverload overload, int word)
        : base(owner, nodeId, cancelled, word)
    {
        _overload = overload;
    }

    // The ports the call was made with.
    public FunctionSignature Signature => _overload.FunctionSignature ?? FunctionSignature.Empty;

    public bool Return(IReadOnlyList<object?> outputs)
    {
        var values = new object?[_overload.Outputs.Count];
        for (int i = 0; i < outputs.Count && i < Signature.Outputs.Count; i++)
            values[i] = outputs[i];
        return Post(HasExit(ReturnedExit) ? ReturnedExit : "next", new CallAnswer(Signature.Canonical, values));
    }

    public bool Fail(string reason)
    {
        int at = _overload.OutputIndex(ReasonPort);
        if (!HasExit(FailedExit) || at < 0)
        {
            Abandon();
            return false;
        }
        var values = new object?[_overload.Outputs.Count];
        values[at] = reason;
        return Post(FailedExit, new CallAnswer(Signature.Canonical, values));
    }

    private bool HasExit(string exit)
    {
        foreach (var name in _overload.FlowOutputs)
        {
            if (name == exit)
                return true;
        }
        return false;
    }
}
