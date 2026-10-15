// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Components;

// One per world: every soft body that steps this frame, stepped together. The constraint half of a
// substep is arithmetic on a body's own particles, so the bodies solve side by side on the pool; the
// contact half asks the world's physics and stays on the world thread. Substep by substep each body
// still solves and then collides in its own order, so it moves exactly as it did stepped alone. The
// flush runs inside the same synchronous drain the bodies queued from, so nothing is a frame late. -xlinka
internal sealed class SoftBodyStepBatch
{
    private static readonly ConditionalWeakTable<World, SoftBodyStepBatch> Batches = new();

    private readonly World _world;
    private readonly List<SquishyBody> _pending = new();
    private readonly Action _flush;
    private readonly ParticleStepScheduler _scheduler;
    private SquishyBody[] _running = Array.Empty<SquishyBody>();
    private int _substep;
    private ulong _frame = ulong.MaxValue;
    private bool _scheduled;

    private SoftBodyStepBatch(World world)
    {
        _world = world;
        _flush = Flush;
        _scheduler = new ParticleStepScheduler(SolveOne);
    }

    public static SoftBodyStepBatch For(World world)
        => Batches.GetValue(world, static w => new SoftBodyStepBatch(w));

    public void Enqueue(SquishyBody body, ulong frame)
    {
        // A batch left over from an earlier frame means its drain never came back for it; settle it
        // rather than leave those bodies a frame behind.
        if (_scheduled && frame != _frame && _pending.Count > 0)
            Flush();
        _frame = frame;
        if (!_pending.Contains(body))
            _pending.Add(body);
        if (!_scheduled)
        {
            _scheduled = true;
            _world.RunSynchronously(_flush);
        }
    }

    public void Remove(SquishyBody body) => _pending.Remove(body);

    private void Flush()
    {
        _scheduled = false;
        int count = _pending.Count;
        if (count == 0)
            return;
        if (_running.Length < count)
            _running = new SquishyBody[System.Math.Max(count, 8)];
        _pending.CopyTo(_running, 0);
        _pending.Clear();

        long start = Stopwatch.GetTimestamp();
        int substeps = 0;
        for (int i = 0; i < count; i++)
            substeps = System.Math.Max(substeps, _running[i].BatchedSteps);
        // A world past Running is torn down on this thread a few steps later; no pool work into that.
        bool pooled = count > 1 && _world.State == World.WorldState.Running;
        try
        {
            for (int s = 0; s < substeps; s++)
            {
                _substep = s;
                if (pooled)
                    _scheduler.Run(count);
                else
                    for (int i = 0; i < count; i++)
                        SolveOne(i);
                for (int i = 0; i < count; i++)
                    _running[i].ResolveBatchedStep(s);
            }
        }
        finally
        {
            // The budget is about the world thread's wall time, so each body is charged its share of
            // the batch rather than the CPU time it burned on a pool thread.
            double each = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / count;
            for (int i = 0; i < count; i++)
            {
                _running[i].FinishBatchedStep(each);
                _running[i] = null!;
            }
        }
    }

    private void SolveOne(int index) => _running[index].SolveBatchedStep(_substep);
}
