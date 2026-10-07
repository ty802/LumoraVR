// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

// Provider refreshes on a world's thread, a few milliseconds of them a frame. A machine receiving an
// import gets every material, mesh and texture provider of it at once, and building each one makes engine
// objects: all of them in one frame held the receiver for seconds. A refresh runs at once while the frame's
// share lasts and nothing is waiting ahead of it, so one edit still lands the frame it is made; the rest
// wait their turn in order, at least one a frame so a single slow one still gets through. Refreshes asked
// for off the world thread always wait here. -xlinka
internal sealed class AssetRefreshQueue
{
    private const double BudgetMs = 3.0;

    private static readonly ConditionalWeakTable<World, AssetRefreshQueue> Queues = new();

    private readonly World _world;
    private readonly Queue<Action> _pending = new();
    private readonly Action _pump;
    private bool _scheduled;
    private ulong _frame = ulong.MaxValue;
    private double _spentMs;

    private AssetRefreshQueue(World world)
    {
        _world = world;
        _pump = Pump;
    }

    private static AssetRefreshQueue For(World world) => Queues.GetValue(world, static w => new AssetRefreshQueue(w));

    // Refreshes still queued in this world. Read by the settle gate.
    public static int PendingFor(World world)
    {
        if (world == null || !Queues.TryGetValue(world, out var queue))
            return 0;
        lock (queue._pending)
        {
            return queue._pending.Count;
        }
    }

    // World thread only. True when the refresh ran.
    public static bool TryRunNow(World world, Action refresh)
    {
        var queue = For(world);
        lock (queue._pending)
        {
            if (queue._pending.Count > 0)
                return false;
        }
        if (!queue.HasShare())
            return false;
        queue.RunTimed(refresh);
        return true;
    }

    public static void Enqueue(World world, Action refresh)
    {
        var queue = For(world);
        lock (queue._pending)
        {
            queue._pending.Enqueue(refresh);
            if (queue._scheduled)
                return;
            queue._scheduled = true;
        }
        world.RunSynchronously(queue._pump);
    }

    private bool HasShare()
    {
        ulong frame = _world.Time.UpdateIndex;
        if (frame != _frame)
        {
            _frame = frame;
            _spentMs = 0;
        }
        return _spentMs < BudgetMs;
    }

    private void RunTimed(Action refresh)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            refresh();
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"AssetRefreshQueue: a provider refresh threw: {ex}");
        }
        _spentMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void Pump()
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            PumpShare(started);
        }
        finally
        {
            Engine.ChargeLoad(started);
        }
    }

    // The waiting ones also stop at the frame's shared load allowance. An edit made while nothing waits
    // still runs inline through TryRunNow, whatever a backlog elsewhere has spent.
    private void PumpShare(long started)
    {
        bool first = true;
        while (first || (HasShare() && !Engine.LoadAllowanceSpent(started, Engine.LoadShareAll)))
        {
            first = false;
            Action next;
            lock (_pending)
            {
                if (_pending.Count == 0)
                {
                    _scheduled = false;
                    return;
                }
                next = _pending.Dequeue();
            }
            HasShare();
            RunTimed(next);
        }
        _world.RunOnNextUpdate(_pump);
    }
}
