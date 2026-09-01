// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using System.Threading.Tasks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// The world a running task belongs to, plus the element whose lifetime it rides on. One per started
// task, carried by the async execution context, so every await point further down the chain can find
// its way home without the author threading a world reference through every helper.
internal sealed class WorldTaskScope
{
    public readonly World World;
    public readonly IWorldElement? Owner;

    public WorldTaskScope(World world, IWorldElement? owner)
    {
        World = world;
        Owner = owner;
    }

    // A task outlives neither its world nor the worker that started it. A null owner is a world-scoped
    // task: it keeps going until the world itself is gone.
    public bool IsAlive
    {
        get
        {
            if (World == null || World.IsDisposed || World.IsDestroyed)
                return false;
            if (Owner == null)
                return true;
            return !Owner.IsDestroyed
                && Owner.World != null
                && Owner is not Worker { IsRemoved: true };
        }
    }

    public string Describe()
        => Owner != null ? $"{Owner.GetType().Name} in '{World?.Name}'" : $"world '{World?.Name}'";
}

// Context switching for world tasks. A task started through Worker.StartTask / World.StartTask can
// move itself between the world thread and the thread pool by awaiting one of these, instead of
// hand-marshalling every result back through RunSynchronously with a closure.
//
//   StartTask(async () =>
//   {
//       await WorldContext.ToBackground();
//       var bytes = File.ReadAllBytes(path);   // off the world thread, blocking is fine here
//       await WorldContext.ToWorld();
//       Field.Value = Decode(bytes);           // back on the world thread, datamodel writes are legal
//   });
//
// No SynchronizationContext is installed on purpose. A plain `await SomethingAsync()` keeps resuming
// wherever the task library puts it, same as it does today; the only way back onto the world thread is
// to ask for it. Implicit marshalling reads well right up until something inside a library await
// deadlocks against the update loop. -xlinka
public static class WorldContext
{
    private static readonly AsyncLocal<WorldTaskScope?> _scope = new AsyncLocal<WorldTaskScope?>();

    private static readonly Action<Action> _runContinuation = continuation =>
    {
        // An escaped exception on a pool thread takes the process with it, and this delegate is the
        // last frame we own before the runtime's.
        try { continuation(); }
        catch (Exception ex) { LumoraLogger.Error($"World task continuation failed: {ex}"); }
    };

    private static readonly Action<Task, object?> _reportFailure = (task, state) =>
    {
        var error = task.Exception;
        if (error == null)
            return;
        LumoraLogger.Error($"World task failed ({(state as WorldTaskScope)?.Describe()}): {error.GetBaseException()}");
    };

    internal static WorldTaskScope? Scope => _scope.Value;

    public static World? CurrentWorld => _scope.Value?.World;

    public static IWorldElement? CurrentOwner => _scope.Value?.Owner;

    // False once the worker that started this task (or its world) is gone. The context switches unwind a
    // task on their own, but a long stretch of background work between them can check this and give up
    // early rather than finish computing a result nothing will read.
    public static bool IsOwnerAlive => _scope.Value?.IsAlive ?? false;

    // Resume on the world thread. Completes inline when the caller is already there.
    public static ToWorldAwaitable ToWorld() => new ToWorldAwaitable(_scope.Value);

    // Resume on the thread pool. Completes inline when the caller is already off the world thread.
    public static ToBackgroundAwaitable ToBackground() => new ToBackgroundAwaitable(_scope.Value);

    // Resume on the world thread during the next update, never the current one.
    public static NextUpdateAwaitable NextUpdate() => new NextUpdateAwaitable(_scope.Value);

    // Gives an async method that was NOT started through StartTask something to switch against - an
    // awaitable library call like Slot.LoadObjectAsync, which its caller awaits rather than fires off.
    // Only legal from INSIDE an async method: the compiler's builder rewinds the execution context
    // around the synchronous prologue, so the scope reaches this method's own continuations and stops
    // there. Called from an ordinary method it would leak into the caller. Call it once, up front.
    public static void Enter(World world, IWorldElement? owner = null)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        _scope.Value = new WorldTaskScope(world, owner);
    }

    internal static void Start(World? world, IWorldElement? owner, Func<Task>? body)
    {
        if (body == null || world == null || world.IsDisposed)
            return;

        var scope = new WorldTaskScope(world, owner);
        var previous = _scope.Value;

        // Set before the body runs so the state machine captures a context holding the scope; the
        // restore afterwards only rewinds OUR logical thread, the captured copy is a snapshot.
        _scope.Value = scope;
        try
        {
            Watch(body(), scope);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"World task threw before its first suspension ({scope.Describe()}): {ex}");
        }
        finally
        {
            _scope.Value = previous;
        }
    }

    internal static void Start<T>(World? world, IWorldElement? owner, Func<T, Task>? body, T argument)
    {
        if (body == null || world == null || world.IsDisposed)
            return;

        var scope = new WorldTaskScope(world, owner);
        var previous = _scope.Value;

        _scope.Value = scope;
        try
        {
            Watch(body(argument), scope);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"World task threw before its first suspension ({scope.Describe()}): {ex}");
        }
        finally
        {
            _scope.Value = previous;
        }
    }

    // Faulted only. A task we cancelled because its owner went away finishes Canceled (the builder maps
    // an OperationCanceledException that way), and that is the ordinary teardown path, not an error.
    private static void Watch(Task? task, WorldTaskScope scope)
    {
        if (task == null || task.IsCompletedSuccessfully || task.IsCanceled)
            return;

        task.ContinueWith(_reportFailure, scope, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // Every world-bound resume lands here. A continuation whose owner is already gone is finished off
    // the world thread instead: the awaiter throws the moment it runs, so the state machine unwinds
    // through its own finally blocks without a dead worker ever seeing the world again. Dropping the
    // continuation outright would be simpler and would strand every using/finally in the task. -xlinka
    internal static void ResumeOnWorld(WorldTaskScope scope, Action continuation, bool nextUpdate)
    {
        if (!scope.IsAlive)
        {
            ResumeOnBackground(continuation);
            return;
        }

        if (nextUpdate)
            scope.World.RunOnNextUpdate(continuation);
        else
            scope.World.RunSynchronously(continuation);
    }

    internal static void ResumeOnBackground(Action continuation)
        => ThreadPool.QueueUserWorkItem(_runContinuation, continuation, preferLocal: false);

    // Called from GetResult, never from OnCompleted. An exception thrown out of OnCompleted escapes the
    // state machine entirely: the builder hands it to the thread pool as an unhandled exception and the
    // process goes down with it. One thrown from GetResult is inside MoveNext's own try and lands in the
    // task where it belongs. -xlinka
    internal static void RequireContext(WorldTaskScope? scope, string awaitable)
    {
        if (scope == null)
        {
            throw new InvalidOperationException(
                $"await WorldContext.{awaitable}() needs a world task context. Start the task with Worker.StartTask / World.StartTask, or open one with WorldContext.Enter.");
        }
    }

    internal static void ThrowIfGone(WorldTaskScope? scope, string awaitable)
    {
        RequireContext(scope, awaitable);
        if (!scope!.IsAlive)
            throw new OperationCanceledException($"World task cancelled, its owner is gone ({scope.Describe()}).");
    }
}
