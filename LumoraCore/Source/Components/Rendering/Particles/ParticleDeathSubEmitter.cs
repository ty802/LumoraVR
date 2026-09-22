// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Spawns into the target system the moment one of this system's particles dies.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleDeathSubEmitter : ParticleSubEmitterBase
{
    internal override ParticleSimModule CreateSimModule()
        => new Lumora.Simulation.Particles.Modules.ParticleDeathSubEmitter();

    internal override void PushParameters(ParticleSimModule module)
        => PushShared((SubEmitterBase)module);
}
