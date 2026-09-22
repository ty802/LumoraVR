// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Initializers run once per particle, at birth, and cost nothing per frame after that. They MULTIPLY
// what the emitter produced rather than replacing it (except the velocity ones, which are explicitly
// overrides), so stacking a lifetime initializer on an emitter that already sets lifetimes scales it
// instead of throwing the emitter's work away.
//
// Angles are in DEGREES on these components - that is what the rest of the engine's rotation fields
// use - and converted to radians on the way into the simulation. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLifetimeInitializer : ParticleModuleBase
{
    // Multiplier on the emitted lifetime.
    public readonly Sync<float> Lifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        Lifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new LifetimeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((LifetimeConstantInitializer)module).Lifetime = Lifetime.Value;
}
