// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Fixed spin around the facing axis, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocityInitializer : ParticleModuleBase
{
    public readonly Sync<float> AngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        AngularVelocity.Value = 180f;
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocityConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((AngularVelocityConstantInitializer)module).AngularVelocity = AngularVelocity.Value * (MathF.PI / 180f);
}
