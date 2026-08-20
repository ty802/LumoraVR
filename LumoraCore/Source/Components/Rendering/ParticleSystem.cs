// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Helio.UI;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using Lumora.Simulation.Particles.Modules;
using SimEmitters = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Datamodel front end for one particle simulation. The simulation itself lives in LumoraSimulation and
// knows nothing about slots, sync or networking; this component owns one instance of it, keeps its
// emitter and module set in step with the emitter/module COMPONENTS pointed at it, and pushes field
// values in every frame. The platform hook only renders the finished buffers.
//
// Simulation runs per peer on the world thread, in SYSTEM-SLOT-LOCAL space, so the whole effect rides
// its slot rigidly and nothing about it goes over the wire. Two peers match because they run the same
// seed through the same deterministic generator, not because anyone synchronises particles.
//
// With no emitter components attached the system falls back to its built-in disc spray driven by the
// fields below, so the original single-component setups keep working untouched. -xlinka
[ComponentCategory("Rendering")]
[DefaultUpdateOrder(-3000)] // after IK/soft bodies; emitters on animated slots see final poses
public sealed class ParticleSystem : ImplementableComponent, IInputUpdateReceiver, ICustomInspectorUI
{
    // ceiling on one system, whatever MaxParticles asks for
    public const int MaxParticlesPerSystem = 8192;

    // ceiling on every system in one world put together; first come, first served
    public const int MaxParticlesPerWorld = 65536;

    [Group("Emission")]
    public readonly Sync<int> MaxParticles = new();
    public readonly Sync<float> EmissionRate = new();
    public readonly Sync<int> BurstCount = new();
    public readonly Sync<float> BurstInterval = new();
    [Group("Shape")]
    public readonly Sync<float3> EmitterExtents = new();
    public readonly Sync<float> SpawnHeight = new();
    [Group("Lifetime")]
    public readonly Sync<float> Lifetime = new();
    public readonly Sync<float> LifetimeVariance = new();
    [Group("Size")]
    public readonly Sync<float> StartSize = new();
    public readonly Sync<float> EndSize = new();
    [Group("Motion")]
    public readonly Sync<float> InitialSpeed = new();
    public readonly Sync<float> SpeedVariance = new();
    public readonly Sync<float> Spread = new();
    public readonly Sync<float> Gravity = new();
    [Group("Color")]
    public readonly Sync<colorHDR> StartColor = new();
    public readonly Sync<colorHDR> EndColor = new();
    public readonly Sync<float> EmissionStrength = new();
    [Group("Advanced")]
    public readonly Sync<int> Seed = new();
    public readonly Sync<int> RenderQueue = new();

    // seconds; 0 runs one variable step per frame. Fixing it keeps the effect consistent across frame
    // rates and keeps peers on the same seed in step instead of diverging
    public readonly Sync<float> FixedTimeStep = new();

    // ignored below a few thousand particles, and by any system holding a module that keeps
    // per-particle state across a chunk
    public readonly Sync<bool> MultiThreaded = new();

    // Per-world capacity accounting. Attached to the World rather than held in a static dictionary so a
    // closed world takes its budget with it instead of leaking an entry forever. -xlinka
    private static readonly ConditionalWeakTable<World, WorldParticleBudget> WorldBudgets = new();

    private sealed class WorldParticleBudget
    {
        private readonly Dictionary<ParticleSystem, int> _reserved = new();

        public int Total { get; private set; }

        public int SystemCount => _reserved.Count;

        public int Reserve(ParticleSystem system, int request)
        {
            _reserved.TryGetValue(system, out int current);
            int others = Total - current;
            int granted = System.Math.Clamp(request, 0, System.Math.Max(0, MaxParticlesPerWorld - others));
            _reserved[system] = granted;
            Total = others + granted;
            return granted;
        }

        public void Release(ParticleSystem system)
        {
            if (_reserved.TryGetValue(system, out int reserved))
            {
                _reserved.Remove(system);
                Total -= reserved;
            }
        }
    }

    private ParticleSimulation? _sim;
    private PositionSimulatorModule? _positionSim;
    private RotationSimulatorModule? _rotationSim;
    private GravityForce? _gravity;
    private SizeColorEnvelope? _envelope;
    private DiscSprayEmitter? _builtinEmitter;

    private readonly List<ParticleEmitterBase> _emitters = new();
    private readonly List<ParticleModuleBase> _modules = new();
    private bool _bindingsDirty = true;
    private bool _hasRotationOutput;
    private bool _registered;
    private int _lastSeed;
    private int _grantedCapacity;
    private float _burstTimer;
    private float _builtinAccumulator;

    private static readonly float3[] EmptyFloat3 = System.Array.Empty<float3>();
    private static readonly floatQ[] EmptyFloatQ = System.Array.Empty<floatQ>();
    private static readonly colorHDR[] EmptyColor = System.Array.Empty<colorHDR>();

    public int ParticleCount => _sim?.ParticleCount ?? 0;

    // granted after the per-system and per-world caps
    public int Capacity => _grantedCapacity;

    public int ModuleCount => _sim?.ModuleCount ?? 0;

    public int EmitterCount => _sim?.EmitterCount ?? 0;

    // bumped once per completed step; the hook skips frames that didn't change
    public int RenderVersion { get; private set; }

    public float3[] RenderPositions => _sim?.RenderData.Positions.Array ?? EmptyFloat3;

    public float3[] RenderSizes => _sim?.RenderData.Sizes.Array ?? EmptyFloat3;

    public colorHDR[] RenderColors => _sim?.RenderData.Colors.Array ?? EmptyColor;

    public floatQ[] RenderRotations => _sim?.RenderData.Rotations.Array ?? EmptyFloatQ;

    // true once something actually rotates particles; the hook skips the math otherwise
    public bool HasRotations => _rotationSim != null || _hasRotationOutput;

    public override void OnInit()
    {
        base.OnInit();

        MaxParticles.Value = 360;
        EmissionRate.Value = 18f;
        BurstCount.Value = 5;
        BurstInterval.Value = 0.42f;
        EmitterExtents.Value = new float3(24.5f, 0f, 24.5f);
        SpawnHeight.Value = 0.035f;
        Lifetime.Value = 0.72f;
        LifetimeVariance.Value = 0.18f;
        StartSize.Value = 0.065f;
        EndSize.Value = 0.018f;
        InitialSpeed.Value = 1.25f;
        SpeedVariance.Value = 0.45f;
        Spread.Value = 0.34f;
        Gravity.Value = -1.15f;
        StartColor.Value = new colorHDR(0.90f, 1.00f, 0.94f, 0.95f);
        EndColor.Value = new colorHDR(1.00f, 0.56f, 0.88f, 0.0f);
        EmissionStrength.Value = 1.4f;
        // Per-instance seed so two systems in the same world don't emit in lockstep.
        Seed.Value = System.Random.Shared.Next(1, int.MaxValue);
        RenderQueue.Value = 60;
        FixedTimeStep.Value = 0f;
    }

    public override void OnStart()
    {
        base.OnStart();
        var input = Engine.Current?.InputInterface;
        if (input != null)
        {
            input.RegisterInputEventReceiver(this);
            _registered = true;
        }
    }

    public override void OnDestroy()
    {
        if (_registered)
            Engine.Current?.InputInterface?.UnregisterInputEventReceiver(this);
        if (World != null && WorldBudgets.TryGetValue(World, out var budget))
            budget.Release(this);
        _sim?.Dispose();
        _sim = null;
        base.OnDestroy();
    }

    internal void RegisterEmitter(ParticleEmitterBase emitter)
    {
        if (_emitters.Contains(emitter))
            return;
        _emitters.Add(emitter);
        _bindingsDirty = true;
    }

    internal void UnregisterEmitter(ParticleEmitterBase emitter)
    {
        if (_emitters.Remove(emitter))
            _bindingsDirty = true;
    }

    internal void RegisterModule(ParticleModuleBase module)
    {
        if (_modules.Contains(module))
            return;
        _modules.Add(module);
        _bindingsDirty = true;
    }

    internal void UnregisterModule(ParticleModuleBase module)
    {
        if (_modules.Remove(module))
            _bindingsDirty = true;
    }

    // created on first update; null before that and after destruction
    internal ParticleSimulation? Simulation => _sim;

    public void BeforeInputUpdate() { }

    public void AfterInputUpdate()
    {
        if (IsDestroyed || !Enabled)
            return;

        // Input dispatch is global, so a backgrounded world's systems would keep burning CPU on a sim
        // nobody can see (the render node is frozen while backgrounded anyway). Skip it; the sim resumes
        // from its frozen state when the world is focused again. - xlinka
        if (World?.Focus == World.WorldFocus.Background)
            return;

        EnsureSimulation();
        var sim = _sim!;

        if (Seed.Value != _lastSeed)
        {
            _lastSeed = Seed.Value;
            sim.Reseed(_lastSeed == 0 ? 1 : _lastSeed);
            sim.Clear();
            _builtinAccumulator = 0f;
            _burstTimer = 0f;
        }

        float dt = System.Math.Clamp(World?.Time.SmoothDelta ?? (1f / 60f), 0f, 0.05f);

        SyncBindings();
        PushSystemParameters(dt);
        PushSubsystemParameters(dt);

        sim.Update(dt);
        RenderVersion = sim.RenderVersion;
    }

    private void EnsureSimulation()
    {
        if (_sim != null)
            return;

        _lastSeed = Seed.Value;
        _sim = new ParticleSimulation(_lastSeed == 0 ? 1 : _lastSeed);

        // Built-ins first so they sort ahead of component modules inside their phase: the system's own
        // gravity has always run before attached forces, and the order is visible in the result.
        _positionSim = _sim.AddModule<PositionSimulatorModule>();
        _positionSim.OrderOffset = -1;
        _gravity = _sim.AddModule<GravityForce>();
        _gravity.OrderOffset = -1;
        _envelope = _sim.AddModule<SizeColorEnvelope>();
        _envelope.OrderOffset = -1;
        _bindingsDirty = true;
    }

    // only runs when something was actually attached, detached, or destroyed
    private void SyncBindings()
    {
        if (!_bindingsDirty)
            return;
        _bindingsDirty = false;
        var sim = _sim!;

        for (int i = _emitters.Count - 1; i >= 0; i--)
        {
            var component = _emitters[i];
            if (component.IsDestroyed)
            {
                if (component.SimEmitter != null)
                {
                    sim.RemoveEmitter(component.SimEmitter);
                    component.SimEmitter = null;
                }
                _emitters.RemoveAt(i);
                continue;
            }
            if (component.SimEmitter == null)
            {
                var emitter = component.CreateSimEmitter();
                sim.AddEmitter(emitter);
                component.SimEmitter = emitter;
            }
        }

        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            var component = _modules[i];
            if (component.IsDestroyed)
            {
                if (component.SimModule != null)
                {
                    sim.RemoveModule(component.SimModule);
                    component.SimModule = null;
                }
                _modules.RemoveAt(i);
                continue;
            }
            if (component.SimModule == null)
            {
                var module = component.CreateSimModule();
                sim.AddModule(module);
                component.SimModule = module;
            }
            // Registration order decides pipeline order inside a phase. The index is the order: the
            // loop runs backwards so a removal at i cannot shift anything still to be visited.
            component.SimModule!.OrderOffset = i;
        }

        // The built-in envelope is the whole of the system's start/end size and colour behaviour. It
        // stays on until an attached module takes over colour or size, otherwise the two would fight
        // over the same buffers and whichever ran last would silently win.
        bool appearanceOverridden = false;
        bool needsRotation = false;
        _hasRotationOutput = false;
        for (int i = 0; i < _modules.Count; i++)
        {
            appearanceOverridden |= _modules[i].OverridesAppearance;
            needsRotation |= _modules[i].RequiresRotationSimulation;
            _hasRotationOutput |= _modules[i].ProducesRotation;
        }
        _envelope!.Enabled = !appearanceOverridden;

        // Rotation integration is added on demand and then kept: a system that has spun particles must
        // keep integrating them or they would freeze mid-spin the moment the module was detached.
        if (needsRotation && _rotationSim == null)
        {
            _rotationSim = sim.AddModule<RotationSimulatorModule>();
            _rotationSim.OrderOffset = -1;
        }

        SyncBuiltinEmitter();
    }

    // on exactly while no emitter component is feeding this system
    private void SyncBuiltinEmitter()
    {
        var sim = _sim!;
        bool wantBuiltin = _emitters.Count == 0;
        if (wantBuiltin == (_builtinEmitter != null))
            return;

        if (wantBuiltin)
        {
            _builtinEmitter = sim.AddEmitter<DiscSprayEmitter>();
        }
        else
        {
            sim.RemoveEmitter(_builtinEmitter!);
            _builtinEmitter = null;
        }
    }

    private void PushSystemParameters(float dt)
    {
        var sim = _sim!;

        int request = System.Math.Clamp(MaxParticles.Value, 1, MaxParticlesPerSystem);
        _grantedCapacity = World != null
            ? WorldBudgets.GetValue(World, static _ => new WorldParticleBudget()).Reserve(this, request)
            : request;
        sim.MaxParticleCount = System.Math.Max(1, _grantedCapacity);

        sim.FixedTimeStep = System.Math.Clamp(FixedTimeStep.Value, 0f, 0.1f);
        sim.JobScheduler = MultiThreaded.Value ? ParallelJobScheduler.Instance : InlineJobScheduler.Instance;
        sim.GlobalTransform = Slot.LocalToWorld;

        _gravity!.Axis = float3.Up;
        _gravity.Magnitude = Gravity.Value;

        var envelope = _envelope!;
        envelope.StartSize = MathF.Max(0.001f, StartSize.Value);
        envelope.EndSize = MathF.Max(0.001f, EndSize.Value);
        envelope.StartColor = StartColor.Value;
        envelope.EndColor = EndColor.Value;

        if (_builtinEmitter != null)
            PushBuiltinEmitter(dt);
    }

    // rate plus timed bursts, matching how the single-component system has always emitted: whole
    // particles from a carried accumulator, plus a burst every BurstInterval seconds
    private void PushBuiltinEmitter(float dt)
    {
        var emitter = _builtinEmitter!;
        emitter.Extents = EmitterExtents.Value;
        emitter.SpawnHeight = SpawnHeight.Value;
        emitter.Speed = InitialSpeed.Value;
        emitter.SpeedVariance = SpeedVariance.Value;
        emitter.Spread = Spread.Value;
        emitter.Lifetime = Lifetime.Value;
        emitter.LifetimeVariance = LifetimeVariance.Value;

        _builtinAccumulator += MathF.Max(0f, EmissionRate.Value) * dt;
        int emit = (int)_builtinAccumulator;
        _builtinAccumulator -= emit;

        float burstInterval = BurstInterval.Value;
        int burstCount = System.Math.Clamp(BurstCount.Value, 0, 64);
        if (burstInterval > 0f && burstCount > 0)
        {
            _burstTimer += dt;
            while (_burstTimer >= burstInterval)
            {
                emit += burstCount;
                _burstTimer -= burstInterval;
            }
        }
        emitter.WantsToEmitCount = emit;
    }

    // unconditional, not dirty-flagged: these are a handful of field copies against a per-particle sim,
    // and a missed push would silently do nothing until the next unrelated edit
    private void PushSubsystemParameters(float dt)
    {
        var systemSlot = Slot;

        for (int i = 0; i < _emitters.Count; i++)
        {
            var component = _emitters[i];
            var emitter = component.SimEmitter;
            if (emitter == null)
                continue;
            if (component.IsDestroyed)
            {
                _bindingsDirty = true;
                continue;
            }
            var toSystem = component.Slot.GetLocalToSpaceMatrix(systemSlot);
            component.PushParameters(emitter, in toSystem);
            emitter.WantsToEmitCount = component.ConsumeEmissionCount(dt);
        }

        for (int i = 0; i < _modules.Count; i++)
        {
            var component = _modules[i];
            var module = component.SimModule;
            if (module == null)
                continue;
            if (component.IsDestroyed)
            {
                _bindingsDirty = true;
                continue;
            }
            module.Enabled = component.Enabled && component.Slot?.IsActive == true;
            if (module.Enabled)
                component.PushParameters(module);
        }
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Particles", $"{ParticleCount} / {Capacity}");
        InspectorStats.AddRow(ui, "Emitters", _builtinEmitter != null ? "built-in disc" : _emitters.Count.ToString());
        InspectorStats.AddRow(ui, "Modules", $"{_modules.Count} attached, {ModuleCount} in pipeline");
        InspectorStats.AddRow(ui, "Step", FixedTimeStep.Value > 0f ? $"fixed {FixedTimeStep.Value * 1000f:0.#} ms" : "variable");
        InspectorStats.AddRow(ui, "Threading", MultiThreaded.Value ? "parallel chunks" : "inline");
        InspectorStats.AddRow(ui, "Rotation", _rotationSim != null ? "integrated"
            : _hasRotationOutput ? "set at birth" : "off");
        if (World != null && WorldBudgets.TryGetValue(World, out var budget))
            InspectorStats.AddRow(ui, "World budget", $"{budget.Total} / {MaxParticlesPerWorld} across {budget.SystemCount} systems");
    }
}
