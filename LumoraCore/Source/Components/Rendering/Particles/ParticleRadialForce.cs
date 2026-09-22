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
