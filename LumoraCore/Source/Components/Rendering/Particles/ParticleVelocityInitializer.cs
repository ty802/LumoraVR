// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Replaces the velocity outright with a fixed vector, ignoring the emission direction.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleVelocityInitializer : ParticleModuleBase
{
    // Initial velocity in the system's simulation space.
    public readonly Sync<float3> Velocity = new();

    public override void OnInit()
    {
        base.OnInit();
        Velocity.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new VelocityConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((VelocityConstantInitializer)module).Velocity = Velocity.Value;
}
