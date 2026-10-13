// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Calls from one script to a Function of another, so a script can be a library for its author's others. The
// rules are the messages' rules: only between scripts of one author, so no one's script runs someone else's
// with their rights, and only between scripts running on the same machine, a Shared script calling the Shared
// scripts on the host and a Local or Author script the copies running on its own machine. The first script on
// the target with a Function of that name and exactly those ports takes the call. It runs in that script's own
// turn, as that script, never inside the call that asked for it. -xlinka
internal static class ScriptFunctionCalls
{
    public static void Start(ScriptContext caller, CallTicket ticket, object? target, string function, FunctionSignature signature,
        object?[] arguments)
    {
        if (target is not Slot slot || slot.IsDestroyed || !ScriptReads.Visible(caller, slot))
        {
            ticket.Fail("there is no target slot to call on");
            return;
        }
        string where = $"'{(string.IsNullOrEmpty(slot.Name.Value) ? "(slot)" : slot.Name.Value)}'";
        bool here = caller.IsLocal || caller.OnAuthorMachine;
        bool mine = false;
        string? ports = null;
        foreach (var host in slot.GetComponents<ScriptHost>())
        {
            if (host.IsDestroyed || ScriptPrincipal.ForElement(host)?.AuthorKey != caller.Principal.AuthorKey)
                continue;
            mine = true;
            var runtime = here ? host.LocalRuntime : host.SharedRuntime;
            if (runtime is not { CanRun: true, DrivesOnly: false } || runtime.Principal.AuthorKey != caller.Principal.AuthorKey)
                continue;
            foreach (var entry in runtime.Runner.Compiled.Functions)
            {
                if (entry.Name != function)
                    continue;
                if (entry.Signature.Canonical == signature.Canonical)
                {
                    runtime.EnqueueFunctionCall(caller, ticket, function, arguments, where);
                    return;
                }
                ports ??= entry.Signature.Canonical;
            }
        }
        if (ports != null)
            ticket.Fail($"{function} on {where} takes {ports}, not {signature.Canonical}");
        else if (!mine)
            ticket.Fail($"no script by the same author on {where} has a function {function}");
        else if (!AnyRunning(slot, caller, here))
            ticket.Fail($"the script on {where} is not running{(here ? " on this machine" : string.Empty)}");
        else
            ticket.Fail($"no script on {where} has a function {function}");
    }

    private static bool AnyRunning(Slot slot, ScriptContext caller, bool here)
    {
        foreach (var host in slot.GetComponents<ScriptHost>())
        {
            var runtime = here ? host.LocalRuntime : host.SharedRuntime;
            if (runtime is { CanRun: true, DrivesOnly: false } && runtime.Principal.AuthorKey == caller.Principal.AuthorKey)
                return true;
        }
        return false;
    }
}
