// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Applies a random euler rotation, in degrees, drawn per particle from a per-axis range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRotation3DRangeInitializer : ParticleModuleBase
{
    internal override bool ProducesRotation => true;

    public readonly Sync<float3> MinEulerAngles = new();
    public readonly Sync<float3> MaxEulerAngles = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxEulerAngles.Value = new float3(360f, 360f, 360f);
    }

    internal override ParticleSimModule CreateSimModule() => new Rotation3DEulerRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var rotation = (Rotation3DEulerRangeInitializer)module;
        rotation.MinEulerAngles = MinEulerAngles.Value;
        rotation.MaxEulerAngles = MaxEulerAngles.Value;
    }
}
