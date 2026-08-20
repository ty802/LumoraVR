// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;

namespace Lumora.Simulation.Particles;

// How the simulation fans its per-particle work out. The default is inline on the calling thread;
// a caller with a thread pool it trusts can swap in a parallel implementation. Chunks are disjoint
// index ranges, so no module needs a lock - see the threading contract on ParticleSimModule.
public interface IParticleJobScheduler
{
    // Runs job once for every index in [0, jobCount) and returns when all are done.
    void Schedule(Action<int> job, int jobCount);
}

// Runs every chunk on the calling thread, in order. Cheapest option and always correct.
public sealed class InlineJobScheduler : IParticleJobScheduler
{
    public static readonly InlineJobScheduler Instance = new();

    public void Schedule(Action<int> job, int jobCount)
    {
        for (int i = 0; i < jobCount; i++)
            job(i);
    }
}

// Fans chunks across the thread pool. Worth it only for systems large enough that the per-chunk
// dispatch cost disappears into the work, which is why the simulation keeps a minimum chunk size
// rather than splitting every system it owns.
public sealed class ParallelJobScheduler : IParticleJobScheduler
{
    public static readonly ParallelJobScheduler Instance = new();

    public void Schedule(Action<int> job, int jobCount)
    {
        if (jobCount <= 1)
        {
            if (jobCount == 1)
                job(0);
            return;
        }
        Parallel.For(0, jobCount, job);
    }
}
