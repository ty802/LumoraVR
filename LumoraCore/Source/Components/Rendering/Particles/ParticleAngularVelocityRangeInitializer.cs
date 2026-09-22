// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Spin around the facing axis drawn per particle from a range, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocityRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinAngularVelocity = new();
    public readonly Sync<float> MaxAngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        MinAngularVelocity.Value = -180f;
        MaxAngularVelocity.Value = 180f;
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocityRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var angular = (AngularVelocityRangeInitializer)module;
        angular.MinAngularVelocity = MinAngularVelocity.Value * (MathF.PI / 180f);
        angular.MaxAngularVelocity = MaxAngularVelocity.Value * (MathF.PI / 180f);
    }
}
