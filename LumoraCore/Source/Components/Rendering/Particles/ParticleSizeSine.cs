// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Pulses the size on a sine, per axis. Embers breathing, a shield shimmering, anything that should not
// sit perfectly still.
//
// SeedPhase offsets each particle's phase from its birth seed. With it off, every particle in a burst
// breathes in unison and the whole thing reads as one object rather than a crowd.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeSine : ParticleModuleBase
{
    // Cycles per second, per axis.
    public readonly Sync<float3> Frequency = new();

    // Radians, per axis.
    public readonly Sync<float3> PhaseOffset = new();

    public readonly Sync<float3> MinMultiplier = new();
    public readonly Sync<float3> MaxMultiplier = new();

    public readonly Sync<bool> SeedPhase = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        Frequency.Value = float3.One;
        MinMultiplier.Value = new float3(0.9f, 0.9f, 0.9f);
        MaxMultiplier.Value = new float3(1.1f, 1.1f, 1.1f);
        SeedPhase.Value = true;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeSineMultiplier();

    internal override void PushParameters(ParticleSimModule module)
    {
        var sine = (SizeSineMultiplier)module;
        sine.Frequency = Frequency.Value;
        sine.PhaseOffset = PhaseOffset.Value;
        sine.MinMultiplier = MinMultiplier.Value;
        sine.MaxMultiplier = MaxMultiplier.Value;
        sine.SeedPhase = SeedPhase.Value;
    }
}
