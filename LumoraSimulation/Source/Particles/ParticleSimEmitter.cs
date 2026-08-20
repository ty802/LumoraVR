// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// A source of new particles. An emitter only decides WHERE a particle appears and which way it is
// pointing; how fast it goes, how long it lives and what it looks like is the initializers' job -
// which is why an emitter reports, per attribute, whether it wrote that attribute at all. Anything it
// declines to write the core fills with a neutral default before initializers run.
//
// Emission count is pushed in from outside (WantsToEmitCount) rather than computed here, so rate,
// bursts and gating stay with whoever owns the wall clock. -xlinka
public abstract class ParticleSimEmitter : IDisposable
{
    public ParticleSimulation Simulation { get; private set; } = null!;

    public bool IsDisposed { get; private set; }

    public int WantsToEmitCount { get; set; }

    public abstract bool InitializesRotations { get; }
    public abstract bool InitializesLifetimes { get; }
    public abstract bool InitializesDirections { get; }
    public abstract bool InitializesColors { get; }
    public abstract bool InitializesSizes { get; }

    // Fill the given slices. Returns how many entries were actually written, which may be fewer than
    // requested (a mesh emitter with no mesh writes nothing); the core trims the difference.
    public abstract int Emit(
        int count,
        Span<float3> positions,
        Span<floatQ> rotations,
        Span<float3> directions,
        Span<colorHDR> colors,
        Span<float3> sizes,
        Span<float> lifetimes);

    internal void Attach(ParticleSimulation simulation)
    {
        if (Simulation != null)
            throw new InvalidOperationException("Emitter is already attached to a simulation");
        Simulation = simulation;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        Simulation = null!;
    }
}

// An emitter whose shape is placed by a transform relative to the simulation space.
public abstract class TransformableSimEmitter : ParticleSimEmitter
{
    // Shape-local to simulation-local transform. Identity means the shape sits at the origin.
    public float4x4 Transform = float4x4.Identity;

    protected float3 TransformPoint(in float3 point) => Transform.MultiplyPoint(point);

    protected float3 TransformVector(in float3 vector) => Transform.MultiplyVector(vector);

    protected float3 TransformDirection(in float3 direction)
    {
        var v = Transform.MultiplyVector(direction);
        float len = v.Length;
        return len > 1e-6f ? v * (direction.Length / len) : v;
    }
}
