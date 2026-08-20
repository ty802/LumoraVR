// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Velocity drag: bleeds speed off every particle (smoke that slows, sparks that die down).
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleDrag : ParticleModuleBase
{
    // Fraction of the current speed lost per second. 1 loses all of it in a second.
    public readonly Sync<float> Drag = new();

    public override void OnInit()
    {
        base.OnInit();
        Drag.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new VelocityDrag();

    internal override void PushParameters(ParticleSimModule module)
        => ((VelocityDrag)module).Drag = Drag.Value;
}
