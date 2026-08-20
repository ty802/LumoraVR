// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Shared front end for the sub-emitters: this system's particles spawn particles into ANOTHER system.
// The two systems keep their own simulations, capacities and modules; only the spawned particles cross
// over, rebased from one simulation space into the other on arrival.
//
// Pointing a sub-emitter at its own system is allowed and behaves exactly as you would expect - the
// children land in the same buffers and compete for the same capacity - so a runaway chain is bounded
// by MaxParticles rather than by anything clever. -xlinka
[ComponentCategory("Rendering/Particles")]
public abstract class ParticleSubEmitterBase : ParticleModuleBase
{
    // The system spawned particles are handed to.
    public readonly SyncRef<ParticleSystem> TargetSystem = new();

    // Inclusive lower bound of the per-event child count.
    public readonly Sync<int> EmitMin = new();

    // Exclusive upper bound of the per-event child count.
    public readonly Sync<int> EmitMax = new();

    public readonly Sync<bool> InheritOrientation = new();
    public readonly Sync<bool> InheritScale = new();
    public readonly Sync<bool> InheritColor = new();
    public readonly Sync<bool> InheritLifetime = new();

    public readonly Sync<SubEmissionDirectionMode> DirectionMode = new();

    // Direction used by the Forced and Orientation modes.
    public readonly Sync<float3> Direction = new();

    // Blend the resulting direction toward a random one. 1 is fully random.
    public readonly Sync<float> RandomDirectionWeight = new();

    public override void OnInit()
    {
        base.OnInit();
        EmitMin.Value = 1;
        EmitMax.Value = 2;
        Direction.Value = float3.Up;
    }

    // Copy the shared settings, then let the target system resolve.
    protected void PushShared(SubEmitterBase module)
    {
        module.EmitMin = EmitMin.Value;
        module.EmitMax = EmitMax.Value;

        // The target's simulation only exists once it has updated at least once; until then the
        // sub-emitter simply produces nothing rather than forcing the other system to spin up.
        module.SetTarget(TargetSystem.Target?.Simulation);

        var parameters = module.Parameters;
        parameters.InheritOrientation = InheritOrientation.Value;
        parameters.InheritScale = InheritScale.Value;
        parameters.InheritColor = InheritColor.Value;
        parameters.InheritLifetime = InheritLifetime.Value;
        parameters.DirectionMode = DirectionMode.Value;
        parameters.Direction = Direction.Value;
        parameters.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}

// Spawns into the target system the moment one of this system's particles is born.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleBirthSubEmitter : ParticleSubEmitterBase
{
    internal override ParticleSimModule CreateSimModule()
        => new Lumora.Simulation.Particles.Modules.ParticleBirthSubEmitter();

    internal override void PushParameters(ParticleSimModule module)
        => PushShared((SubEmitterBase)module);
}

// Spawns into the target system the moment one of this system's particles dies.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleDeathSubEmitter : ParticleSubEmitterBase
{
    internal override ParticleSimModule CreateSimModule()
        => new Lumora.Simulation.Particles.Modules.ParticleDeathSubEmitter();

    internal override void PushParameters(ParticleSimModule module)
        => PushShared((SubEmitterBase)module);
}

// Spawns into the target system continuously along each particle's life, at a rate per second. Trails
// of sparks off a falling ember, smoke off a rocket.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLifetimeSubEmitter : ParticleSubEmitterBase
{
    // Children per second, per living particle.
    public readonly Sync<float> Rate = new();

    public override void OnInit()
    {
        base.OnInit();
        Rate.Value = 10f;
    }

    internal override ParticleSimModule CreateSimModule()
        => new Lumora.Simulation.Particles.Modules.ParticleLifetimeSubEmitter();

    internal override void PushParameters(ParticleSimModule module)
    {
        PushShared((SubEmitterBase)module);
        ((Lumora.Simulation.Particles.Modules.ParticleLifetimeSubEmitter)module).Rate = Rate.Value;
    }
}
