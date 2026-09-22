// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Replaces the velocity with a vector drawn per particle from a per-axis range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleVelocityRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinVelocity = new();
    public readonly Sync<float3> MaxVelocity = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxVelocity.Value = float3.One;
    }

    internal override ParticleSimModule CreateSimModule() => new VelocityRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var velocity = (VelocityRangeInitializer)module;
        velocity.MinVelocity = MinVelocity.Value;
        velocity.MaxVelocity = MaxVelocity.Value;
    }
}
