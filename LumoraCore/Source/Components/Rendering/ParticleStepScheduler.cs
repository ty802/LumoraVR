// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Core.Components;

// Reused by one world's particle batch. Each index is claimed once, the caller also does work, and
// Run waits for every step in flight before the world can render or dispose any simulation. No captured
// delegates, tasks, or per-flush wait handles; Monitor owns the reusable completion wait.
//
// Run waits for steps, never for the pool. It used to wait until every worker it queued had run, and a
// worker the pool had not got to yet held the world thread for as long as the pool was busy: a big import
// arriving keeps every pool thread compressing and decoding, and receivers froze for seconds inside this
// wait with every step long done by the caller. Now the caller closes the batch once every index is
// claimed, and a worker that starts after that finds it closed and leaves. A worker counts itself in
// before it looks, and the next batch lays itself out only once nobody is counted, so a late one never
// sees half a batch. -xlinka
internal sealed class ParticleStepScheduler : IThreadPoolWorkItem
{
    private readonly Action<int> _step;
    private readonly object _completion = new();
    private int _next, _count, _active, _open, _running, _queued;
    private Exception? _failure;

    public ParticleStepScheduler(Action<int> step) => _step = step;

    public void Run(int count)
    {
        if (count <= 0) return;
        if (Interlocked.Exchange(ref _running, 1) != 0)
            throw new InvalidOperationException("A particle batch cannot overlap itself.");
        try
        {
            if (count == 1 || Environment.ProcessorCount == 1)
            {
                for (int i = 0; i < count; i++) _step(i);
                return;
            }
            WaitForActive();
            _count = count;
            _failure = null;
            Volatile.Write(ref _next, -1);
            Volatile.Write(ref _open, 1);
            // Workers still waiting in the pool's queue from earlier batches join whichever batch is open
            // when they run, so they count toward this one and a busy pool does not pile up more.
            int workers = System.Math.Min(count - 1, Environment.ProcessorCount - 1) - Volatile.Read(ref _queued);
            for (int i = 0; i < workers; i++)
            {
                Interlocked.Increment(ref _queued);
                try
                {
                    // Simulations only touch their own buffers; they must not inherit world-thread
                    // permissions or synchronization context through ExecutionContext flow.
                    ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
                }
                catch (Exception ex)
                {
                    Interlocked.Decrement(ref _queued);
                    Interlocked.CompareExchange(ref _failure, ex, null);
                }
            }
            Drain();
            Interlocked.Exchange(ref _open, 0);
            WaitForActive();
            var failure = _failure;
            _failure = null;
            if (failure != null) throw new AggregateException("Particle batch failed.", failure);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    // Most tails finish within a few microseconds of the caller's last system. Briefly spin before
    // parking the world thread, then use a real wait for a longer step.
    private void WaitForActive()
    {
        var spin = new SpinWait();
        while (Volatile.Read(ref _active) != 0 && !spin.NextSpinWillYield) spin.SpinOnce();
        lock (_completion)
            while (Volatile.Read(ref _active) != 0) Monitor.Wait(_completion);
    }

    void IThreadPoolWorkItem.Execute()
    {
        Interlocked.Increment(ref _active);
        Interlocked.Decrement(ref _queued);
        try
        {
            if (Volatile.Read(ref _open) != 0)
                Drain();
        }
        finally
        {
            if (Interlocked.Decrement(ref _active) == 0)
                lock (_completion) Monitor.PulseAll(_completion);
        }
    }

    private void Drain()
    {
        int index;
        while ((index = Interlocked.Increment(ref _next)) < _count)
        {
            try { _step(index); }
            catch (Exception ex) { Interlocked.CompareExchange(ref _failure, ex, null); }
        }
    }
}
