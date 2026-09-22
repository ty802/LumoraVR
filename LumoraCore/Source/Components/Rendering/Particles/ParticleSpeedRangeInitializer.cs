// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emission direction to a speed drawn per particle from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpeedRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinSpeed = new();
    public readonly Sync<float> MaxSpeed = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxSpeed.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new SpeedRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var speed = (SpeedRangeInitializer)module;
        speed.MinSpeed = MinSpeed.Value;
        speed.MaxSpeed = MaxSpeed.Value;
    }
}
