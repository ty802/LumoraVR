// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Simulation.Particles;

// Where a module runs inside one simulation step. The pipeline is explicitly phased instead of
// order-only: forces have to land on velocity BEFORE the integrator consumes it (semi-implicit Euler,
// the stable one - integrate first and a force takes a frame to show up and gravity under-reads),
// appearance has to run after the state it reads is final, and orientation has to be able to look at
// the velocity the integrator just used. Within a phase, modules run in their owner's order. -xlinka
public enum ParticleSimPhase
{
    // Birth-value writers. No per-frame work; they only touch newly emitted particles.
    Initializer = 0,

    // Writes velocity / angular velocity. Gravity, drag, turbulence, attractors.
    Force = 100,

    // Consumes velocity: position and rotation integration.
    Integrate = 200,

    // Colour and size over lifetime, colour by speed, size modifiers.
    Appearance = 300,

    // Orientation overrides that need the final position and velocity.
    Orientation = 400,

    // Last-word writers into the render buffers.
    Output = 500,
}

// Which per-particle render columns a module reads or writes. The core uses this to work out, for
// each module, whether it should read the value the particle was BORN with or the value an earlier
// module in the same frame already computed. Get this wrong and chaining two appearance modules
// either compounds every frame (runaway) or silently discards the first one. -xlinka
[Flags]
public enum ParticleRenderChannel
{
    None = 0,
    Position = 1,
    Rotation = 2,
    Size = 4,
    Color = 8,
}

// One unit of per-particle behaviour. A module owns whatever private per-particle columns it needs
// (a velocity buffer, a pivot buffer) and is handed the exact same birth/death callbacks the core
// uses on its own columns, so private state stays index-aligned with the shared state.
//
// Modules must be chunk-safe: SimulateChunk can be called concurrently for disjoint ranges when a
// parallel job scheduler is attached, so a module may only touch particles inside the range it was
// handed and may not mutate its own fields from inside the loop. -xlinka
public abstract class ParticleSimModule : IDisposable
{
    private int _orderOffset;

    // The simulation this module belongs to. Assigned once, on registration.
    public ParticleSimulation Simulation { get; private set; } = null!;

    // Inactive modules keep their state but are skipped by every pass.
    public bool IsActive { get; private set; } = true;

    private bool _enabled = true;

    // Owner switch. A disabled module keeps its configuration and its private columns but is dropped
    // from every pass; it is separate from IsActive, which the core drives on its own to resolve
    // duplicate single-instance modules.
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
                return;
            _enabled = value;
            Simulation?.MarkModuleOrderDirty();
        }
    }

    public bool IsDisposed { get; private set; }

    // Sort key inside the phase. Owners set it from the order of their components.
    public int OrderOffset
    {
        get => _orderOffset;
        set
        {
            if (value == _orderOffset)
                return;
            _orderOffset = value;
            Simulation?.MarkModuleOrderDirty();
        }
    }

    public abstract ParticleSimPhase Phase { get; }

    // Order within the newborn-initialisation pass, low first. Modules that OWN a column other
    // modules write into (the position and rotation integrators) run negative so the column exists
    // before an initializer reaches for it.
    public virtual int InitPriority => 0;

    // False for modules where a second instance would fight the first (the integrators).
    public virtual bool AllowMultipleInstances => true;

    // False for modules that keep per-particle state they read AND write inside SimulateChunk, or that
    // touch shared state there. One such module in a system drops the whole system back to inline
    // execution - correctness first, and the alternative is a race nobody can reproduce on demand.
    public virtual bool SupportsParallelChunks => true;

    public virtual ParticleRenderChannel Consumes => ParticleRenderChannel.None;

    public virtual ParticleRenderChannel Produces => ParticleRenderChannel.None;

    // Of the columns this module consumes, the ones no earlier module produced this frame - so they
    // must be read from the starting data rather than the render buffer. Maintained by the core.
    public ParticleRenderChannel ReadsFromStarting { get; internal set; }

    // Colour column to read: birth colour if nothing upstream has written one yet.
    protected Span<Lumora.Core.Math.colorHDR> SourceColors
        => (ReadsFromStarting & ParticleRenderChannel.Color) != 0 ? Simulation.StartingColors : Simulation.RenderColors;

    // Size column to read, same rule as SourceColors.
    protected Span<Lumora.Core.Math.float3> SourceSizes
        => (ReadsFromStarting & ParticleRenderChannel.Size) != 0 ? Simulation.StartingSizes : Simulation.RenderSizes;

    // Rotation column to read, same rule as SourceColors.
    protected Span<Lumora.Core.Math.floatQ> SourceRotations
        => (ReadsFromStarting & ParticleRenderChannel.Rotation) != 0 ? Simulation.StartingRotations : Simulation.RenderRotations;

    internal int InsertionIndex;

    internal void Attach(ParticleSimulation simulation)
    {
        if (Simulation != null)
            throw new InvalidOperationException("Module is already attached to a simulation");
        Simulation = simulation;
        Initialized();
    }

    internal void SetActive(bool active)
    {
        if (active == IsActive)
            return;
        IsActive = active;
        if (!active)
            TrimParticles(0);
        else if (Simulation.ParticleCount > 0)
        {
            InitializeNewParticles(0, Simulation.ParticleCount);
            NewParticlesInitialized(0, Simulation.ParticleCount);
        }
    }

    // Called once, immediately after the module is attached to a simulation.
    public virtual void Initialized() { }

    // Called whenever the module set changes - re-resolve any module cross-references here.
    public virtual void ModulesUpdated() { }

    // Once per step, before any emission or simulation. Cache per-frame constants here.
    public virtual void PrepareUpdate(float deltaTime) { }

    // The per-particle work. May run concurrently for disjoint ranges.
    public abstract void SimulateChunk(int offset, int count, float deltaTime);

    // Once per step, after every chunk has finished. Single threaded.
    public virtual void PostprocessUpdate(float deltaTime) { }

    // Grow private columns for freshly emitted particles and seed them from starting data.
    public virtual void InitializeNewParticles(int index, int count) { }

    // Second newborn pass, after every module has done its InitializeNewParticles.
    public virtual void NewParticlesInitialized(int index, int count) { }

    // A module added to a simulation that already has live particles must size its per-particle
    // columns to the existing population or its first SimulateChunk slices out of range - and late
    // adds are the NORMAL case, not the odd one: a wrapper's live-apply rebind re-creates its sim
    // module mid-life. The default runs BOTH newborn passes over the existing population (some modules
    // split their columns across the two, and skipping the second leaves them misaligned forever);
    // side effects like a fresh trails module giving live particles trails are exactly what attaching
    // the module should mean. Value initializers override this to nothing: a late-attached initializer
    // only affects future births, it does not rewrite live values. -xlinka
    public virtual void BackfillExistingParticles(int count)
    {
        InitializeNewParticles(0, count);
        NewParticlesInitialized(0, count);
    }

    // Particles about to be compacted away. Indices are still valid for this call only.
    public virtual void ParticlesDying(ReadOnlySpan<int> dyingIndexes) { }

    // Apply the death compaction plan to every private column.
    public virtual void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount) { }

    // Drop private columns down to a new count without moving anything.
    public virtual void TrimParticles(int newCount) { }

    // Report any simulation this module spawns particles into.
    public virtual void CollectSubEmissionTargets(List<ParticleSimulation> targets) { }

    protected virtual void OnDispose() { }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        OnDispose();
        Simulation = null!;
    }
}
