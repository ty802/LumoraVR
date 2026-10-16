// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Engine work that has to run on the main thread, finished a few milliseconds of it a frame: texture,
// mesh, skin and cubemap builds handed back from the threads that prepared them. Handed back with a plain
// deferred call they all ran in the one flush that followed, and an avatar or an import arriving is a
// hundred of them landing together: the frame they landed in lasted seconds, outside anything the world's
// own frame report could see. At least one runs a frame, so one big build still gets through; any single
// build slow enough to hold a frame on its own says so by name.
//
// Time alone does not hold it: handing a texture over returns at once and the copy to the GPU happens
// later in the frame, where no timer here sees it. So work that carries data says how much, and a frame
// takes no more than ByteBudget of it. -xlinka
public static class MainThreadWork
{
    private const double BudgetMs = 4.0;
    private const long ByteBudget = 32L * 1024 * 1024;
    private const double SlowMs = 100.0;

    private static readonly ConcurrentQueue<(string Label, Action Work, long Bytes)> _queue = new();
    private static int _attached;

    public static int Pending => _queue.Count;

    // Any thread.
    public static void Post(string label, Action work, long bytes = 0)
    {
        _queue.Enqueue((label, work, bytes));
        if (Volatile.Read(ref _attached) == 0 && Interlocked.CompareExchange(ref _attached, 1, 0) == 0)
            Callable.From(Attach).CallDeferred();
    }

    private static void Attach()
    {
        if (global::Godot.Engine.GetMainLoop() is SceneTree tree)
            tree.ProcessFrame += Drain;
        else
            Volatile.Write(ref _attached, 0);
        Drain();
    }

    private static void Drain()
    {
        if (_queue.IsEmpty)
            return;
        long started = Stopwatch.GetTimestamp();
        long bytes = 0;
        bool first = true;
        // Also bounded by the frame's shared load allowance, of which this takes what the worlds left.
        while (_queue.TryPeek(out var next)
            && (first || (Stopwatch.GetElapsedTime(started).TotalMilliseconds < BudgetMs && bytes + next.Bytes <= ByteBudget
                && !Lumora.Core.Engine.LoadAllowanceSpent(started, Lumora.Core.Engine.LoadShareAll)))
            && _queue.TryDequeue(out var item))
        {
            first = false;
            bytes += item.Bytes;
            long itemStarted = Stopwatch.GetTimestamp();
            try
            {
                item.Work();
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"MainThreadWork: {item.Label} threw: {ex}");
            }
            double ms = Stopwatch.GetElapsedTime(itemStarted).TotalMilliseconds;
            if (ms >= SlowMs)
                LumoraLogger.Warn($"MainThreadWork: {item.Label} took {ms:0} ms on the main thread ({_queue.Count} waiting)");
        }
        Lumora.Core.Engine.ChargeLoad(started);
    }
}
