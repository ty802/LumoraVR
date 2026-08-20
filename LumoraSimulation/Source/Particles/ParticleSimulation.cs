// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// A CPU particle simulation: struct-of-arrays state, a set of emitters, and a phase-ordered module
// pipeline. Knows nothing about worlds, networking, slots or rendering - it is handed a delta time and
// produces a frame of render data. Everything that needs the outside world (collision, emission rate,
// where the emitter shape sits) arrives as plain values pushed in by the host.
//
// One step, in order:
//   1. advance time, clamp to capacity
//   2. PrepareUpdate on every active module
//   3. age particles, collect the dead, compact every column from one shared move plan
//   4. emit: emitters and queued sub-emissions fill the starting columns, then newborn init runs
//   5. simulate: modules run per phase over chunks (inline, or fanned out by a job scheduler)
//   6. PostprocessUpdate, hand sub-emission batches to their targets, bump the render version
//
// Deaths are compacted with a swap-back plan computed once and replayed on every column, shared and
// private alike - that is the invariant the whole thing rests on. -xlinka
public sealed class ParticleSimulation : IDisposable
{
    // Chunk size for the per-particle passes. Also the parallel-dispatch granularity.
    public const int ParticlesPerChunk = 2048;

    private const int DefaultMaxParticles = 1024;

    private readonly ParticleStartingData _starting = new();
    private readonly ParticleStateData _state = new();
    private readonly ParticleRenderData _render = new();

    private readonly List<ParticleSimModule> _modules = new();
    private readonly List<ParticleSimModule> _ordered = new();
    private readonly List<ParticleSimModule> _initOrdered = new();
    private readonly List<ParticleSimEmitter> _emitters = new();
    private readonly List<ParticleSimulation> _subEmissionTargets = new();

    private readonly List<IncomingBatch> _incoming = new();
    private readonly List<IncomingBatch> _incomingScratch = new();
    private readonly object _incomingLock = new();

    private int[] _dying = new int[128];
    private int _dyingCount;
    private ParticleMove[] _moves = new ParticleMove[128];
    private int _moveCount;
    private int[] _emitCounts = System.Array.Empty<int>();

    private bool _moduleOrderDirty = true;
    private bool _chunksParallelSafe = true;
    private float _fixedStepAccumulator;
    private int _insertionCounter;
    private int _seed;

    private readonly struct IncomingBatch
    {
        public readonly SubEmissionBatch Batch;
        public readonly SubEmissionManager Owner;

        public IncomingBatch(SubEmissionBatch batch, SubEmissionManager owner)
        {
            Batch = batch;
            Owner = owner;
        }
    }

    public ParticleSimulation(int seed = 0)
    {
        _seed = seed;
        Random = new SeededRandom(seed);
        SubEmission = new SubEmissionManager(this);
    }

    public int ParticleCount { get; private set; }

    // Hard ceiling. Emission stops at it; lowering it below the live count trims immediately.
    public int MaxParticleCount { get; set; } = DefaultMaxParticles;

    public int FreeCapacity => System.Math.Max(0, MaxParticleCount - ParticleCount);

    // Seconds simulated since the last reset.
    public float Time { get; private set; }

    // Fixed simulation step in seconds; 0 runs one variable step per update. A fixed step makes the
    // result independent of frame rate, which is what makes two peers running the same seed actually
    // match instead of merely starting the same.
    public float FixedTimeStep { get; set; }

    // Cap on fixed sub-steps per update, so a hitch cannot spiral into a freeze.
    public int MaxSubSteps { get; set; } = 4;

    // Simulation-local to world transform. Only used to rebase cross-simulation emission.
    public float4x4 GlobalTransform = float4x4.Identity;

    // Deterministic source for everything decided at birth. Reseeding replays the same effect.
    public SeededRandom Random { get; private set; }

    public SubEmissionManager SubEmission { get; }

    // Inline by default. Assign a parallel scheduler to fan chunks across the thread pool.
    public IParticleJobScheduler JobScheduler { get; set; } = InlineJobScheduler.Instance;

    // Below this many particles the simulation stays inline whatever scheduler is attached.
    public int ParallelThreshold { get; set; } = ParticlesPerChunk * 2;

    public bool IsDisposed { get; private set; }

    public int ModuleCount => _modules.Count;

    public int EmitterCount => _emitters.Count;

    // Frame counter for the render data. Bumped once per completed step.
    public int RenderVersion => _render.Version;

    public ParticleRenderData RenderData => _render;

    // Shared columns. Modules slice these; the core owns their lifetime.
    public Span<float3> StartingPositions => _starting.Positions.AsSpan();
    public Span<floatQ> StartingRotations => _starting.Rotations.AsSpan();
    public Span<float3> StartingDirections => _starting.Directions.AsSpan();
    public Span<colorHDR> StartingColors => _starting.Colors.AsSpan();
    public Span<float3> StartingSizes => _starting.Sizes.AsSpan();
    public Span<float> StartingLifetimes => _starting.Lifetimes.AsSpan();
    public Span<uint> StartingSeeds => _starting.Seeds.AsSpan();
    public Span<float> CurrentLifetimes => _state.Lifetimes.AsSpan();
    public Span<float> NormalizedProgressions => _state.NormalizedProgression.AsSpan();
    public Span<float3> RenderPositions => _render.Positions.AsSpan();
    public Span<floatQ> RenderRotations => _render.Rotations.AsSpan();
    public Span<float3> RenderSizes => _render.Sizes.AsSpan();
    public Span<colorHDR> RenderColors => _render.Colors.AsSpan();

    // Raised after the module set is rebuilt, so a host can refresh cached lookups.
    public event Action? ModulesUpdated;

    // MODULES AND EMITTERS

    public T AddModule<T>() where T : ParticleSimModule, new()
    {
        var module = new T();
        AddModule(module);
        return module;
    }

    public void AddModule(ParticleSimModule module)
    {
        module.Attach(this);
        module.InsertionIndex = _insertionCounter++;
        _modules.Add(module);
        MarkModuleOrderDirty();
    }

    public void RemoveModule(ParticleSimModule module)
    {
        if (module.Simulation != this)
            throw new InvalidOperationException("Module does not belong to this simulation");
        _modules.Remove(module);
        _ordered.Remove(module);
        _initOrdered.Remove(module);
        module.Dispose();
        MarkModuleOrderDirty();
    }

    public T AddEmitter<T>() where T : ParticleSimEmitter, new()
    {
        var emitter = new T();
        AddEmitter(emitter);
        return emitter;
    }

    public void AddEmitter(ParticleSimEmitter emitter)
    {
        emitter.Attach(this);
        _emitters.Add(emitter);
    }

    public void RemoveEmitter(ParticleSimEmitter emitter)
    {
        if (!_emitters.Remove(emitter))
            throw new InvalidOperationException("Emitter does not belong to this simulation");
        emitter.Dispose();
    }

    public ParticleSimModule GetModule(int index) => _modules[index];

    public ParticleSimEmitter GetEmitter(int index) => _emitters[index];

    // First active module of the given type, or null.
    public T? TryGetModule<T>() where T : ParticleSimModule
    {
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i] is T typed && typed.IsActive)
                return typed;
        }
        return null;
    }

    public void MarkModuleOrderDirty() => _moduleOrderDirty = true;

    private void EnsureModuleOrder()
    {
        if (!_moduleOrderDirty)
            return;
        _moduleOrderDirty = false;

        _ordered.Clear();
        _ordered.AddRange(_modules);
        _ordered.Sort(CompareModules);

        // Single-instance modules: the first one in pipeline order wins and the rest go inactive rather
        // than silently fighting it (two integrators would each move the particle by a full step).
        var singletons = new HashSet<Type>();
        for (int i = 0; i < _ordered.Count; i++)
        {
            var module = _ordered[i];
            bool active = module.Enabled && (module.AllowMultipleInstances || singletons.Add(module.GetType()));
            module.SetActive(active);
        }

        // Walk the pipeline once and record, per module, which of the columns it reads have already
        // been written this frame. That is what lets appearance modules chain instead of clobber.
        var produced = ParticleRenderChannel.None;
        _chunksParallelSafe = true;
        for (int i = 0; i < _ordered.Count; i++)
        {
            var module = _ordered[i];
            if (!module.IsActive)
                continue;
            module.ReadsFromStarting = module.Consumes & ~produced;
            produced |= module.Produces;
            if (!module.SupportsParallelChunks)
                _chunksParallelSafe = false;
        }

        _initOrdered.Clear();
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _initOrdered.Add(_ordered[i]);
        }
        _initOrdered.Sort(static (a, b) => a.InitPriority.CompareTo(b.InitPriority));

        _subEmissionTargets.Clear();
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].CollectSubEmissionTargets(_subEmissionTargets);
        }
        SubEmission.SetTargets(_subEmissionTargets);

        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].ModulesUpdated();
        }
        ModulesUpdated?.Invoke();
    }

    private static int CompareModules(ParticleSimModule a, ParticleSimModule b)
    {
        int phase = ((int)a.Phase).CompareTo((int)b.Phase);
        if (phase != 0)
            return phase;
        int order = a.OrderOffset.CompareTo(b.OrderOffset);
        return order != 0 ? order : a.InsertionIndex.CompareTo(b.InsertionIndex);
    }

    // STATE

    // Restart the random stream. Same seed, same effect, on every peer.
    public void Reseed(int seed)
    {
        _seed = seed;
        Random.Reseed(seed);
    }

    public int Seed => _seed;

    // Kill every live particle and reset the clock. Modules keep their configuration.
    public void Clear()
    {
        EnsureModuleOrder();
        if (ParticleCount > 0)
            TrimParticles(0);
        Time = 0f;
        _fixedStepAccumulator = 0f;
    }

    // SIMULATION

    // Advance the simulation, honouring FixedTimeStep.
    public void Update(float deltaTime)
    {
        if (IsDisposed || !(deltaTime > 0f))
            return;

        float fixedStep = FixedTimeStep;
        if (fixedStep <= 0f)
        {
            Step(deltaTime);
            return;
        }

        _fixedStepAccumulator += deltaTime;
        int steps = 0;
        int maxSteps = System.Math.Max(1, MaxSubSteps);
        while (_fixedStepAccumulator >= fixedStep && steps < maxSteps)
        {
            Step(fixedStep);
            _fixedStepAccumulator -= fixedStep;
            steps++;
        }
        // Drop the remainder instead of carrying a debt no future frame can pay off: a machine that
        // cannot keep up would otherwise spend every frame further behind, at full sub-step cost.
        if (steps >= maxSteps)
            _fixedStepAccumulator = 0f;
    }

    private void Step(float deltaTime)
    {
        Time += deltaTime;
        EnsureModuleOrder();

        if (ParticleCount > MaxParticleCount)
            TrimParticles(MaxParticleCount);

        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].PrepareUpdate(deltaTime);
        }

        UpdateLifetimes(deltaTime);
        EmitNewParticles();

        if (ParticleCount > 0)
            SimulateModules(deltaTime);

        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].PostprocessUpdate(deltaTime);
        }

        SubEmission.SubmitAll();
        _render.Version++;
    }

    private void SimulateModules(float deltaTime)
    {
        int count = ParticleCount;
        var scheduler = JobScheduler;
        bool parallel = scheduler != null
            && _chunksParallelSafe
            && !ReferenceEquals(scheduler, InlineJobScheduler.Instance)
            && count >= ParallelThreshold;

        if (!parallel)
        {
            RunChunk(0, count, deltaTime);
            return;
        }

        int chunks = (count + ParticlesPerChunk - 1) / ParticlesPerChunk;
        scheduler!.Schedule(index =>
        {
            int offset = index * ParticlesPerChunk;
            RunChunk(offset, System.Math.Min(ParticlesPerChunk, count - offset), deltaTime);
        }, chunks);
    }

    private void RunChunk(int offset, int count, float deltaTime)
    {
        if (count <= 0)
            return;
        for (int i = 0; i < _ordered.Count; i++)
        {
            var module = _ordered[i];
            if (module.IsActive)
                module.SimulateChunk(offset, count, deltaTime);
        }
    }

    private void UpdateLifetimes(float deltaTime)
    {
        int count = ParticleCount;
        if (count == 0)
            return;

        var inverted = _starting.InvertedLifetimes.Slice(0, count);
        var remaining = _state.Lifetimes.Slice(0, count);
        var progression = _state.NormalizedProgression.Slice(0, count);

        _dyingCount = 0;
        for (int i = 0; i < count; i++)
        {
            float left = remaining[i] - deltaTime;
            remaining[i] = left;
            progression[i] = 1f - left * inverted[i];
            if (left < 0f || float.IsNaN(left))
                AddDying(i);
        }
        if (_dyingCount > 0)
            CompactDead();
    }

    private void AddDying(int index)
    {
        if (_dyingCount == _dying.Length)
            System.Array.Resize(ref _dying, _dying.Length * 2);
        _dying[_dyingCount++] = index;
    }

    // Build the swap-back move plan once, hand the dying indices to the modules that care (death
    // sub-emitters read them while they are still valid), then replay the plan across every column.
    private void CompactDead()
    {
        int count = ParticleCount;
        int alive = count - _dyingCount;
        var dying = _dying.AsSpan(0, _dyingCount);

        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].ParticlesDying(dying);
        }

        if (_moves.Length < _dyingCount)
            System.Array.Resize(ref _moves, System.Math.Max(_moves.Length * 2, _dyingCount));
        _moveCount = 0;

        // Fill each hole below the new live count with the last surviving particle, skipping tail
        // entries that are themselves dying. Ascending holes, descending sources, one pass.
        int tail = _dyingCount - 1;
        int src = count - 1;
        for (int i = 0; i < _dyingCount; i++)
        {
            int hole = dying[i];
            if (hole >= alive)
                break;
            while (tail >= 0 && src >= 0 && dying[tail] == src)
            {
                tail--;
                src--;
            }
            if (src < alive)
                break;
            _moves[_moveCount++] = new ParticleMove(hole, src);
            src--;
        }

        var moves = _moves.AsSpan(0, _moveCount);
        _starting.ApplyCompaction(moves, alive);
        _state.ApplyCompaction(moves, alive);
        _render.ApplyCompaction(moves, alive);
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].ParticlesRemoved(moves, alive);
        }
        ParticleCount = alive;
        _dyingCount = 0;
    }

    private void TrimParticles(int newCount)
    {
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_ordered[i].IsActive)
                _ordered[i].TrimParticles(newCount);
        }
        _starting.TrimParticles(newCount);
        _state.TrimParticles(newCount);
        _render.TrimParticles(newCount);
        ParticleCount = newCount;
    }

    // EMISSION

    internal void SubmitSubEmission(SubEmissionBatch batch, SubEmissionManager owner)
    {
        lock (_incomingLock)
            _incoming.Add(new IncomingBatch(batch, owner));
    }

    private void EmitNewParticles()
    {
        int free = FreeCapacity;
        int emitterCount = _emitters.Count;

        int incomingCount = 0;
        lock (_incomingLock)
        {
            for (int i = 0; i < _incoming.Count; i++)
                incomingCount += _incoming[i].Batch.Count;
        }

        int wanted = incomingCount;
        for (int i = 0; i < emitterCount; i++)
            wanted += System.Math.Max(0, _emitters[i].WantsToEmitCount);

        if (wanted == 0 || free == 0)
        {
            DrainIncoming(0);
            return;
        }

        // Over budget: everyone gets the same fraction, so one greedy emitter cannot starve the rest.
        float scale = System.Math.Min(1f, free / (float)wanted);

        if (_emitCounts.Length < emitterCount)
            _emitCounts = new int[System.Math.Max(emitterCount, 8)];

        int budget = free;
        int planned = 0;
        for (int i = 0; i < emitterCount; i++)
        {
            int want = System.Math.Max(0, _emitters[i].WantsToEmitCount);
            int allowed = System.Math.Min(budget, (int)MathF.Round(want * scale));
            _emitCounts[i] = allowed;
            budget -= allowed;
            planned += allowed;
        }
        int incomingBudget = System.Math.Min(budget, (int)MathF.Round(incomingCount * scale));

        int firstNew = ParticleCount;
        int written = 0;
        if (planned > 0)
            written += RunEmitters(emitterCount);
        written += DrainIncoming(incomingBudget);

        if (written == 0)
            return;

        FinishNewParticles(firstNew, written);
    }

    private int RunEmitters(int emitterCount)
    {
        int written = 0;
        for (int i = 0; i < emitterCount; i++)
        {
            int request = _emitCounts[i];
            if (request <= 0)
                continue;

            int at = ParticleCount;
            IncreaseCount(request);

            var positions = _starting.Positions.Slice(at, request);
            var rotations = _starting.Rotations.Slice(at, request);
            var directions = _starting.Directions.Slice(at, request);
            var colors = _starting.Colors.Slice(at, request);
            var sizes = _starting.Sizes.Slice(at, request);
            var lifetimes = _starting.Lifetimes.Slice(at, request);

            var emitter = _emitters[i];
            int produced = emitter.Emit(request, positions, rotations, directions, colors, sizes, lifetimes);
            produced = System.Math.Clamp(produced, 0, request);

            if (produced < request)
            {
                TrimCountOnly(at + produced);
                if (produced == 0)
                    continue;
                positions = positions.Slice(0, produced);
                rotations = rotations.Slice(0, produced);
                directions = directions.Slice(0, produced);
                colors = colors.Slice(0, produced);
                sizes = sizes.Slice(0, produced);
                lifetimes = lifetimes.Slice(0, produced);
            }

            if (!emitter.InitializesRotations)
                rotations.Fill(floatQ.Identity);
            if (!emitter.InitializesDirections)
                directions.Fill(float3.Zero);
            if (!emitter.InitializesColors)
                colors.Fill(colorHDR.White);
            if (!emitter.InitializesSizes)
                sizes.Fill(float3.One);
            if (!emitter.InitializesLifetimes)
                lifetimes.Fill(1f);

            FillSeeds(at, produced);
            ParticleCount = at + produced;
            written += produced;
        }
        return written;
    }

    // Pull queued cross-simulation particles in, rebasing them from their source space.
    private int DrainIncoming(int budget)
    {
        lock (_incomingLock)
        {
            if (_incoming.Count == 0)
                return 0;
            _incomingScratch.Clear();
            _incomingScratch.AddRange(_incoming);
            _incoming.Clear();
        }

        int written = 0;
        var toLocal = GlobalTransform.Inverse;
        for (int b = 0; b < _incomingScratch.Count; b++)
        {
            var incoming = _incomingScratch[b];
            var batch = incoming.Batch;
            int take = System.Math.Min(batch.Count, budget - written);
            if (take > 0)
            {
                var rebase = toLocal * batch.SourceTransform;
                int at = ParticleCount;
                IncreaseCount(take);

                var positions = _starting.Positions.Slice(at, take);
                var rotations = _starting.Rotations.Slice(at, take);
                var directions = _starting.Directions.Slice(at, take);
                var colors = _starting.Colors.Slice(at, take);
                var sizes = _starting.Sizes.Slice(at, take);
                var lifetimes = _starting.Lifetimes.Slice(at, take);
                var source = batch.Particles;

                for (int i = 0; i < take; i++)
                {
                    positions[i] = rebase.MultiplyPoint(source[i].Position);
                    rotations[i] = source[i].Rotation;
                    directions[i] = rebase.MultiplyVector(source[i].Direction);
                    colors[i] = source[i].Color;
                    sizes[i] = source[i].Size;
                    lifetimes[i] = source[i].Lifetime;
                }
                FillSeeds(at, take);
                ParticleCount = at + take;
                written += take;
            }
            incoming.Owner.Return(batch);
        }
        _incomingScratch.Clear();
        return written;
    }

    private void FillSeeds(int index, int count)
    {
        var seeds = _starting.Seeds.Slice(index, count);
        for (int i = 0; i < count; i++)
            seeds[i] = Random.NextUInt();
    }

    // Cache the inverted lifetime, prime the state columns, seed the render columns from the starting
    // values, then run the two newborn passes so private module columns catch up.
    private void FinishNewParticles(int index, int count)
    {
        var lifetimes = _starting.Lifetimes.Slice(index, count);
        var inverted = _starting.InvertedLifetimes.Slice(index, count);
        var remaining = _state.Lifetimes.Slice(index, count);
        var progression = _state.NormalizedProgression.Slice(index, count);

        for (int i = 0; i < count; i++)
        {
            float life = lifetimes[i];
            if (!(life > 1e-5f))
            {
                life = 1e-5f;
                lifetimes[i] = life;
            }
            inverted[i] = 1f / life;
            remaining[i] = life;
            progression[i] = 0f;
        }

        _starting.Positions.Slice(index, count).CopyTo(_render.Positions.Slice(index, count));
        _starting.Rotations.Slice(index, count).CopyTo(_render.Rotations.Slice(index, count));
        _starting.Sizes.Slice(index, count).CopyTo(_render.Sizes.Slice(index, count));
        _starting.Colors.Slice(index, count).CopyTo(_render.Colors.Slice(index, count));

        for (int i = 0; i < _initOrdered.Count; i++)
            _initOrdered[i].InitializeNewParticles(index, count);
        for (int i = 0; i < _initOrdered.Count; i++)
            _initOrdered[i].NewParticlesInitialized(index, count);
    }

    private void IncreaseCount(int newParticles)
    {
        _starting.IncreaseCount(newParticles);
        _state.IncreaseCount(newParticles);
        _render.IncreaseCount(newParticles);
    }

    private void TrimCountOnly(int newCount)
    {
        _starting.TrimParticles(newCount);
        _state.TrimParticles(newCount);
        _render.TrimParticles(newCount);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        for (int i = 0; i < _modules.Count; i++)
            _modules[i].Dispose();
        _modules.Clear();
        _ordered.Clear();
        _initOrdered.Clear();
        for (int i = 0; i < _emitters.Count; i++)
            _emitters[i].Dispose();
        _emitters.Clear();
    }
}
