// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

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
