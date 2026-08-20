// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Pulls (or pushes, negative) particles toward this component's slot. Vortex cores, sinks.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAttractor : ParticleModuleBase
{
    public readonly Sync<float> Strength = new();

    // Force falls off to zero at this distance (0 = no falloff, constant pull).
    public readonly Sync<float> Range = new();

    public override void OnInit()
    {
        base.OnInit();
        Strength.Value = 4f;
        Range.Value = 0f;
    }

    internal override ParticleSimModule CreateSimModule() => new AttractorForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var attractor = (AttractorForce)module;
        // The attractor rides its own slot, so its position is resolved into the system's simulation
        // space every frame rather than baked once.
        var target = System.Target;
        attractor.Center = target != null ? target.Slot.GlobalPointToLocal(Slot.GlobalPosition) : float3.Zero;
        attractor.Strength = Strength.Value;
        attractor.Range = Range.Value;
    }
}
