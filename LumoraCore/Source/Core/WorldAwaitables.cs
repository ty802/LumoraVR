// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Core;

// The three context switches a world task can await. Each one is its own awaiter (GetAwaiter returns
// the struct itself) so an await costs no allocation beyond the state machine's own boxing.
//
// INotifyCompletion and not ICriticalNotifyCompletion, deliberately: the non-critical path is the one
// where the compiler-generated builder captures the ExecutionContext around the continuation, which is
// what carries the task's scope across a thread-pool hop. Implementing the critical interface would
// hand us a continuation with the context stripped and the scope would vanish mid-task. -xlinka

public readonly struct ToWorldAwaitable : INotifyCompletion
{
    private readonly WorldTaskScope? _scope;

    internal ToWorldAwaitable(WorldTaskScope? scope)
    {
        _scope = scope;
    }

    public ToWorldAwaitable GetAwaiter() => this;

    // Already on the world thread: no queue hop, the continuation runs inline on this stack. An owner
    // that died is still reported through GetResult below, so the fast path cannot smuggle a dead
    // worker past the check.
    public bool IsCompleted => _scope != null && _scope.World.IsOnWorldThread;

    // A missing context is reported from GetResult, not from here. An exception out of OnCompleted
    // escapes the state machine and lands on the thread pool unhandled, which is a process kill, so
    // the continuation is handed to the pool and left to fault itself properly. -xlinka
    public void OnCompleted(Action continuation)
    {
        if (_scope == null)
        {
            WorldContext.ResumeOnBackground(continuation);
            return;
        }

        WorldContext.ResumeOnWorld(_scope, continuation, nextUpdate: false);
    }

    public void GetResult() => WorldContext.ThrowIfGone(_scope, "ToWorld");
}

public readonly struct ToBackgroundAwaitable : INotifyCompletion
{
    private readonly WorldTaskScope? _scope;

    internal ToBackgroundAwaitable(WorldTaskScope? scope)
    {
        _scope = scope;
    }

    public ToBackgroundAwaitable GetAwaiter() => this;

    // Only skip the hop when we can PROVE we are off the world thread. Before a world's first update
    // its thread is unknown, and guessing wrong here means blocking file I/O on the update loop.
    public bool IsCompleted
        => _scope != null && _scope.World.WorldThreadId != 0 && !_scope.World.IsOnWorldThread;

    public void OnCompleted(Action continuation) => WorldContext.ResumeOnBackground(continuation);

    // No cancellation check. Leaving the world thread is safe whatever state the owner is in, and a
    // task that gets cancelled on its way back has its background cleanup run first, which is the
    // point of putting the unwind at the world-bound switches. A missing context is still an error,
    // and this is the first switch that would notice it.
    public void GetResult() => WorldContext.RequireContext(_scope, "ToBackground");
}

public readonly struct NextUpdateAwaitable : INotifyCompletion
{
    private readonly WorldTaskScope? _scope;

    internal NextUpdateAwaitable(WorldTaskScope? scope)
    {
        _scope = scope;
    }

    public NextUpdateAwaitable GetAwaiter() => this;

    // Never completes inline. Letting a frame pass IS the operation, so there is no fast path.
    public bool IsCompleted => false;

    public void OnCompleted(Action continuation)
    {
        if (_scope == null)
        {
            WorldContext.ResumeOnBackground(continuation);
            return;
        }

        WorldContext.ResumeOnWorld(_scope, continuation, nextUpdate: true);
    }

    public void GetResult() => WorldContext.ThrowIfGone(_scope, "NextUpdate");
}
