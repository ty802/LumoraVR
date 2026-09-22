// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Spin around all three axes drawn per particle from a per-axis range, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocity3DRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinAngularVelocity = new();
    public readonly Sync<float3> MaxAngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        MinAngularVelocity.Value = new float3(-180f, -180f, -180f);
        MaxAngularVelocity.Value = new float3(180f, 180f, 180f);
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocity3DRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var angular = (AngularVelocity3DRangeInitializer)module;
        angular.MinAngularVelocity = MinAngularVelocity.Value * (MathF.PI / 180f);
        angular.MaxAngularVelocity = MaxAngularVelocity.Value * (MathF.PI / 180f);
    }
}
