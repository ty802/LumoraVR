// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Pushes particles away from this component's slot (negative Force pulls them in), with a choice of
// falloff curve. The distance clamps are not optional decoration on the inverse-square modes: without
// them a particle that wanders close to the centre gets an unbounded impulse and leaves the world.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRadialForce : ParticleModuleBase
{
    public readonly Sync<float> Force = new();
    public readonly Sync<RadialForceMode> Mode = new();

    // Distance below which the falloff stops getting stronger.
    public readonly Sync<float> MinDistance = new();

    // Distance above which the falloff stops getting weaker.
    public readonly Sync<float> MaxDistance = new();

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = 1f;
        Mode.Value = RadialForceMode.InverseSquared;
        MinDistance.Value = 0.05f;
        MaxDistance.Value = float.PositiveInfinity;
    }

    internal override ParticleSimModule CreateSimModule() => new RadialForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var radial = (RadialForce)module;
        var target = System.Target;
        radial.Center = target != null ? target.Slot.GlobalPointToLocal(Slot.GlobalPosition) : float3.Zero;
        radial.Force = Force.Value;
        radial.Mode = Mode.Value;
        radial.MinDistance = MinDistance.Value;
        radial.MaxDistance = MaxDistance.Value;
    }
}

// Pulls each particle back toward the exact point it was BORN at, not toward a shared centre. Dust
// disturbed and resettling, sparks tethered to the surface that threw them.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleOriginForce : ParticleModuleBase
{
    public readonly Sync<float> Force = new();
    public readonly Sync<RadialForceMode> Mode = new();
    public readonly Sync<float> MinDistance = new();
    public readonly Sync<float> MaxDistance = new();

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = 1f;
        Mode.Value = RadialForceMode.Linear;
        MinDistance.Value = 0.01f;
        MaxDistance.Value = float.PositiveInfinity;
    }

    internal override ParticleSimModule CreateSimModule() => new OriginRadialForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var origin = (OriginRadialForce)module;
        origin.Force = Force.Value;
        origin.Mode = Mode.Value;
        origin.MinDistance = MinDistance.Value;
        origin.MaxDistance = MaxDistance.Value;
    }
}

// Feeds spin in continuously. Paired with ParticleAngularDrag it settles at a terminal spin.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpin : ParticleModuleBase
{
    // Angular acceleration per axis, in radians per second squared.
    public readonly Sync<float3> Force = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = new float3(0f, 0f, 1f);
    }

    internal override ParticleSimModule CreateSimModule() => new ConstantAngularVelocityForce();

    internal override void PushParameters(ParticleSimModule module)
        => ((ConstantAngularVelocityForce)module).Force = Force.Value;
}

// Bleeds spin off every particle, the rotational twin of ParticleDrag.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularDrag : ParticleModuleBase
{
    // Fraction of the current spin lost per second. 1 loses all of it in a second.
    public readonly Sync<float> Drag = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        Drag.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocityDrag();

    internal override void PushParameters(ParticleSimModule module)
        => ((AngularVelocityDrag)module).Drag = Drag.Value;
}
