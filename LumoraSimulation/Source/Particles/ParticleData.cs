// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// What each particle was BORN with. Emitters write it, initializers modify it, and per-lifetime
// modules read it as their unchanging reference: a size-over-lifetime module has to scale the size
// the particle started with, not the size it had last frame, or the effect compounds every frame and
// the particle exponentially explodes or vanishes. -xlinka
public sealed class ParticleStartingData
{
    public readonly ParticleBuffer<float3> Positions = new();
    public readonly ParticleBuffer<floatQ> Rotations = new();

    // Emission vector. Magnitude is meaningful: it becomes the initial velocity.
    public readonly ParticleBuffer<float3> Directions = new();

    public readonly ParticleBuffer<colorHDR> Colors = new();
    public readonly ParticleBuffer<float3> Sizes = new();
    public readonly ParticleBuffer<float> Lifetimes = new();

    // 1 / lifetime, cached at birth so the per-frame progression update is a multiply.
    public readonly ParticleBuffer<float> InvertedLifetimes = new();

    // Per-particle random seed, so a module can derive stable per-particle jitter.
    public readonly ParticleBuffer<uint> Seeds = new();

    public void IncreaseCount(int newParticles)
    {
        Positions.IncreaseCount(newParticles);
        Rotations.IncreaseCount(newParticles);
        Directions.IncreaseCount(newParticles);
        Colors.IncreaseCount(newParticles);
        Sizes.IncreaseCount(newParticles);
        Lifetimes.IncreaseCount(newParticles);
        InvertedLifetimes.IncreaseCount(newParticles);
        Seeds.IncreaseCount(newParticles);
    }

    public void ApplyCompaction(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        Positions.ApplyCompaction(moves, newCount);
        Rotations.ApplyCompaction(moves, newCount);
        Directions.ApplyCompaction(moves, newCount);
        Colors.ApplyCompaction(moves, newCount);
        Sizes.ApplyCompaction(moves, newCount);
        Lifetimes.ApplyCompaction(moves, newCount);
        InvertedLifetimes.ApplyCompaction(moves, newCount);
        Seeds.ApplyCompaction(moves, newCount);
    }

    public void TrimParticles(int newCount)
    {
        Positions.TrimParticles(newCount);
        Rotations.TrimParticles(newCount);
        Directions.TrimParticles(newCount);
        Colors.TrimParticles(newCount);
        Sizes.TrimParticles(newCount);
        Lifetimes.TrimParticles(newCount);
        InvertedLifetimes.TrimParticles(newCount);
        Seeds.TrimParticles(newCount);
    }
}

// Per-frame lifetime bookkeeping: time left, and how far through life the particle is.
public sealed class ParticleStateData
{
    public readonly ParticleBuffer<float> Lifetimes = new();

    // 0 at birth, 1 at death. The x axis of every over-lifetime module.
    public readonly ParticleBuffer<float> NormalizedProgression = new();

    public void IncreaseCount(int newParticles)
    {
        Lifetimes.IncreaseCount(newParticles);
        NormalizedProgression.IncreaseCount(newParticles);
    }

    public void ApplyCompaction(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        Lifetimes.ApplyCompaction(moves, newCount);
        NormalizedProgression.ApplyCompaction(moves, newCount);
    }

    public void TrimParticles(int newCount)
    {
        Lifetimes.TrimParticles(newCount);
        NormalizedProgression.TrimParticles(newCount);
    }
}

// The finished frame the renderer consumes: position, orientation, size and colour per live particle,
// plus a version counter so a renderer can skip a frame that produced nothing new. Arrays, not spans,
// because the consumer is a render hook that holds onto them across calls.
public sealed class ParticleRenderData
{
    public readonly ParticleBuffer<float3> Positions = new();
    public readonly ParticleBuffer<floatQ> Rotations = new();
    public readonly ParticleBuffer<float3> Sizes = new();
    public readonly ParticleBuffer<colorHDR> Colors = new();

    // Bumped once per completed simulation step.
    public int Version { get; internal set; }

    public int Count => Positions.Count;

    public void IncreaseCount(int newParticles)
    {
        Positions.IncreaseCount(newParticles);
        Rotations.IncreaseCount(newParticles);
        Sizes.IncreaseCount(newParticles);
        Colors.IncreaseCount(newParticles);
    }

    public void ApplyCompaction(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        Positions.ApplyCompaction(moves, newCount);
        Rotations.ApplyCompaction(moves, newCount);
        Sizes.ApplyCompaction(moves, newCount);
        Colors.ApplyCompaction(moves, newCount);
    }

    public void TrimParticles(int newCount)
    {
        Positions.TrimParticles(newCount);
        Rotations.TrimParticles(newCount);
        Sizes.TrimParticles(newCount);
        Colors.TrimParticles(newCount);
    }
}
