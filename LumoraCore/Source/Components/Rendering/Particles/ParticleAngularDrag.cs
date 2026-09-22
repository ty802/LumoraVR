// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

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
