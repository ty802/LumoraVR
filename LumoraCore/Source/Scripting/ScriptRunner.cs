// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Script;
using Lumora.Warden;

namespace Lumora.Core.Scripting;

// One graph running in one world as one principal. The piece a ScriptHost will own: it compiles through
// the shared cache, links against a context scoped to the principal, and raises events.
//
// A call never starts inside a scope that opens the gate (a system bypass, an authored content scope,
// a config edit): the script would run with the scope's rights instead of its author's. And a user's
// script stops for good once its author is no longer the one behind its byte. Runs on the world
// thread, like any other datamodel code. -xlinka
public sealed class ScriptRunner
{
    public World World { get; }
    public ScriptContext Context { get; }
    public ScriptInstance Instance { get; }
    public bool Stopped { get; private set; }

    public CompileResult Compiled => Instance.Compiled;

    // What starting cost on this machine: the graph's compile, whenever it happened and whether this start
    // found it cached, and loading its module (decode, validate, link, instantiate).
    public double CompileMs { get; private init; }
    public bool CompileCached { get; private init; }
    public double LoadMs { get; private init; }

    private ScriptRunner(World world, ScriptContext context, ScriptInstance instance)
    {
        World = world;
        Context = context;
        Instance = instance;
    }

    // Throws ScriptLinkException when the graph does not compile or cannot link; the diagnostics are on
    // the compile result the message names. Compiles on this thread; a host compiles on a worker first and
    // hands the result to the overload below.
    public static ScriptRunner Start(World world, ScriptGraph graph, ScriptPrincipal principal, IReadOnlyList<object?> bindings,
        ScriptInstanceOptions? options = null, Slot? self = null, bool local = false)
    {
        var compiled = ScriptEnvironment.Compile(graph, out var record, out bool cached);
        return Start(world, compiled, record, cached, principal, bindings, options, self, local);
    }

    public static ScriptRunner Start(World world, CompileResult compiled, ScriptCompileRecord record, bool cached, ScriptPrincipal principal,
        IReadOnlyList<object?> bindings, ScriptInstanceOptions? options = null, Slot? self = null, bool local = false)
    {
        if (!compiled.Success)
            throw new ScriptLinkException("graph does not compile: " + string.Join("; ", compiled.Errors));

        // A host may carry more bindings than this graph declares, blanks a pack left behind; the graph only
        // ever sees as many as it names.
        if (bindings.Count != compiled.Bindings.Count)
        {
            var fitted = new object?[compiled.Bindings.Count];
            for (int i = 0; i < fitted.Length && i < bindings.Count; i++)
                fitted[i] = bindings[i];
            bindings = fitted;
        }

        var context = new ScriptContext(world, principal, self, local);
        long loading = System.Diagnostics.Stopwatch.GetTimestamp();
        var instance = ScriptInstance.Create(compiled, context, bindings, options);
        return new ScriptRunner(world, context, instance)
        {
            CompileMs = record.Milliseconds,
            CompileCached = cached,
            LoadMs = System.Diagnostics.Stopwatch.GetElapsedTime(loading).TotalMilliseconds,
        };
    }

    public ScriptCallResult Raise<T>(in T payload) where T : struct
    {
        if (!CanRun(out var why))
            return new ScriptCallResult(ScriptCallStatus.Trap, null, why, 0);

        Context.Cause = ScriptContext.CauseOf(payload);
        var last = new ScriptCallResult(ScriptCallStatus.NoSuchEvent, null, $"no event takes {typeof(T).Name}", 0);
        foreach (var entry in Instance.Events)
        {
            if (entry.PayloadType != typeof(T))
                continue;
            last = Instance.Raise(entry.NodeId, payload);
            if (!last.Ok)
                break;
        }
        return last;
    }

    // One event node, for a host that routes an event to the nodes listening for it.
    public ScriptCallResult Raise<T>(string nodeId, in T payload) where T : struct
    {
        if (!CanRun(out var why))
            return new ScriptCallResult(ScriptCallStatus.Trap, null, why, 0);
        Context.Cause = ScriptContext.CauseOf(payload);
        return Instance.Raise(nodeId, payload);
    }

    // What follows a Delay, when its timer is due. The timer set it off, not whoever set off the call before it.
    public ScriptCallResult RunTimer(int timer)
    {
        if (!CanRun(out var why))
            return new ScriptCallResult(ScriptCallStatus.Trap, null, why, 0);
        Context.Cause = null;
        return Instance.RunTimer(timer);
    }

    // An exit an await's answer runs, once its work came back. The work set it off, not whoever started it.
    public ScriptCallResult RunAnswer(in ScriptAnswer answer)
    {
        if (!CanRun(out var why))
            return new ScriptCallResult(ScriptCallStatus.Trap, null, why, 0);
        Context.Cause = null;
        return Instance.RunAnswer(answer);
    }

    // Works out one Display that needs no flow.
    public ScriptCallResult RefreshDisplay(string nodeId)
    {
        if (!CanRun(out var why))
            return new ScriptCallResult(ScriptCallStatus.Trap, null, why, 0);
        return Instance.RunDisplay(nodeId);
    }

    public void Stop() => Stopped = true;

    private bool CanRun(out string why)
    {
        why = string.Empty;
        if (Stopped)
        {
            why = "script is stopped";
            return false;
        }
        if (World.IsDisposed || World.IsDestroyed)
        {
            why = "world is gone";
            return false;
        }
        if (PermissionEngine.IsGateOpenOnThisThread || WorldConfigEdit.IsOpen)
        {
            why = "refused to run inside a scope that opens the gate";
            return false;
        }
        if (!Context.Principal.IsStillAuthorIn(World))
        {
            Stopped = true;
            why = "its author is no longer in the world";
            return false;
        }
        return true;
    }
}
