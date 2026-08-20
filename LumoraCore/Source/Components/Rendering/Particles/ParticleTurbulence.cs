// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Cheap procedural turbulence: a swirl field from three phase-shifted sine octaves of the particle
// position, scrolled over time. Not real gradient noise, but at particle scale it reads as wind churn
// and it costs a handful of sin() per particle. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTurbulence : ParticleModuleBase
{
    public readonly Sync<float> Strength = new();

    // Spatial frequency of the swirl. Higher means finer, busier churn.
    public readonly Sync<float> Frequency = new();

    // How fast the field scrolls through itself.
    public readonly Sync<float> ScrollSpeed = new();

    public override void OnInit()
    {
        base.OnInit();
        Strength.Value = 1f;
        Frequency.Value = 2f;
        ScrollSpeed.Value = 0.6f;
    }

    internal override ParticleSimModule CreateSimModule() => new TurbulentForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var turbulence = (TurbulentForce)module;
        turbulence.Mode = ParticleForceMode.Additive;
        turbulence.Strength = Strength.Value;
        turbulence.Frequency = Frequency.Value;
        turbulence.ScrollSpeed = ScrollSpeed.Value;
    }
}
