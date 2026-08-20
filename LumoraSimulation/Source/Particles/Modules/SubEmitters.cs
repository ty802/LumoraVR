// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Where a spawned particle's direction comes from.
public enum SubEmissionDirectionMode
{
    // The authored direction, ignoring the parent entirely.
    Forced,

    // The direction the parent was emitted with.
    InitialDirection,

    // The authored direction rotated by the parent's current orientation.
    Orientation,

    // The parent's current travel direction, unit length.
    VelocityDirection,

    // The parent's current velocity, magnitude and all - the child inherits its speed.
    Velocity,
}

// Shared settings for every kind of sub-emitter: what the child inherits and where it goes.
public sealed class SubEmissionParameters
{
    // Simulation the children are spawned into. Null disables the sub-emitter entirely.
    public ParticleSimulation? Target;

    public bool InheritOrientation;
    public bool InheritScale;
    public bool InheritColor;
    public bool InheritLifetime;

    public SubEmissionDirectionMode DirectionMode = SubEmissionDirectionMode.Forced;
    public float3 Direction = float3.Up;

    // Blend the resulting direction toward a random one. 1 is fully random.
    public float RandomDirectionWeight;

    public bool IsActive => Target != null;
}

// Shared body of the sub-emitters: read one parent particle, produce one child. Everything the child
// gets is read from the parent's CURRENT render state, so a child spawned by a dying particle appears
// exactly where its parent last was rather than where it was born. -xlinka
public abstract class SubEmitterBase : PositionModuleBase
{
    // Inclusive lower bound of the per-event child count.
    public int EmitMin = 1;

    // Exclusive upper bound of the per-event child count.
    public int EmitMax = 2;

    public readonly SubEmissionParameters Parameters = new();

    public override ParticleSimPhase Phase => ParticleSimPhase.Output;

    public override void CollectSubEmissionTargets(List<ParticleSimulation> targets)
    {
        if (Parameters.Target != null)
            targets.Add(Parameters.Target);
    }

    // Retarget the sub-emitter. Goes through here rather than assigning Parameters.Target directly
    // because the owning simulation caches which systems it is allowed to hand batches to, and a
    // target it has not been told about silently receives nothing. -xlinka
    public void SetTarget(ParticleSimulation? target)
    {
        if (ReferenceEquals(Parameters.Target, target))
            return;
        Parameters.Target = target;
        Simulation?.MarkModuleOrderDirty();
    }

    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    protected void SpawnFrom(SubEmissionBatch batch, int index)
    {
        var parameters = Parameters;
        var particle = new SubEmissionParticle
        {
            Position = Simulation.RenderPositions[index],
            Rotation = parameters.InheritOrientation ? Simulation.RenderRotations[index] : floatQ.Identity,
            Size = parameters.InheritScale ? Simulation.RenderSizes[index] : float3.One,
            Color = parameters.InheritColor ? Simulation.RenderColors[index] : colorHDR.White,
            Lifetime = parameters.InheritLifetime ? MathF.Max(Simulation.CurrentLifetimes[index], 1e-3f) : 1f,
        };

        var velocity = PositionModule != null ? PositionModule.Velocities[index] : float3.Zero;
        particle.Direction = parameters.DirectionMode switch
        {
            SubEmissionDirectionMode.InitialDirection => Simulation.StartingDirections[index],
            SubEmissionDirectionMode.Orientation => Simulation.RenderRotations[index] * parameters.Direction,
            SubEmissionDirectionMode.VelocityDirection => velocity.Normalized,
            SubEmissionDirectionMode.Velocity => velocity,
            _ => parameters.Direction,
        };

        if (parameters.RandomDirectionWeight > 0f)
        {
            particle.Direction = ParticleMath.BlendDirection(
                particle.Direction, Simulation.Random.OnUnitSphere, parameters.RandomDirectionWeight);
        }
        batch.Add(particle);
    }

    protected int DrawEmitCount()
    {
        int min = System.Math.Max(0, EmitMin);
        int max = System.Math.Max(min, EmitMax);
        return max > min ? Simulation.Random.Range(min, max) : min;
    }
}

// Spawns children into another simulation the moment a particle is born.
public sealed class ParticleBirthSubEmitter : SubEmitterBase
{
    public override void NewParticlesInitialized(int index, int count)
    {
        var target = Parameters.Target;
        if (target == null)
            return;
        var batch = Simulation.SubEmission.BorrowBatch(target);
        if (batch == null)
            return;
        for (int i = 0; i < count; i++)
        {
            int emit = DrawEmitCount();
            for (int e = 0; e < emit; e++)
                SpawnFrom(batch, index + i);
        }
    }
}

// Spawns children the moment a particle dies. Runs off the dying-index callback, before compaction,
// which is the only point where the dead particle's final state is still readable.
public sealed class ParticleDeathSubEmitter : SubEmitterBase
{
    public override void ParticlesDying(ReadOnlySpan<int> dyingIndexes)
    {
        var target = Parameters.Target;
        if (target == null)
            return;
        var batch = Simulation.SubEmission.BorrowBatch(target);
        if (batch == null)
            return;
        for (int i = 0; i < dyingIndexes.Length; i++)
        {
            int emit = DrawEmitCount();
            for (int e = 0; e < emit; e++)
                SpawnFrom(batch, dyingIndexes[i]);
        }
    }
}

// Spawns children continuously along each particle's life, at a rate per second. Keeps a fractional
// accumulator per particle so a rate below one per frame still emits at the right average instead of
// rounding to nothing - the same trick the emitters use for their own rate. Trails of sparks off a
// falling ember, smoke off a rocket. -xlinka
public sealed class ParticleLifetimeSubEmitter : SubEmitterBase
{
    private readonly ParticleBuffer<float> _accumulators = new();

    // Children per second, per living particle.
    public float Rate = 10f;

    // Reads and writes a per-particle accumulator, so it must not be chunked across threads.
    public override bool SupportsParallelChunks => false;

    public override void InitializeNewParticles(int index, int count)
        => _accumulators.IncreaseCount(count).Fill(0f);

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
        => _accumulators.ApplyCompaction(moves, newCount);

    public override void TrimParticles(int newCount) => _accumulators.TrimParticles(newCount);

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var target = Parameters.Target;
        if (target == null || Rate <= 0f)
            return;
        var batch = Simulation.SubEmission.BorrowBatch(target);
        if (batch == null)
            return;

        var accumulators = _accumulators.Slice(offset, count);
        float step = Rate * deltaTime;
        for (int i = 0; i < count; i++)
        {
            float accumulated = accumulators[i] + step;
            int emit = (int)accumulated;
            accumulators[i] = accumulated - emit;
            for (int e = 0; e < emit; e++)
                SpawnFrom(batch, offset + i);
        }
    }
}
