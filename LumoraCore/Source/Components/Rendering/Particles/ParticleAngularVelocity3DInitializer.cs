// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Fixed spin around all three axes, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocity3DInitializer : ParticleModuleBase
{
    public readonly Sync<float3> AngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        AngularVelocity.Value = new float3(0f, 0f, 180f);
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocity3DConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((AngularVelocity3DConstantInitializer)module).AngularVelocity = AngularVelocity.Value * (MathF.PI / 180f);
}
