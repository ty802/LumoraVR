// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emission direction to a fixed speed. The direction comes from the emitter.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpeedInitializer : ParticleModuleBase
{
    public readonly Sync<float> Speed = new();

    public override void OnInit()
    {
        base.OnInit();
        Speed.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new SpeedConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((SpeedConstantInitializer)module).Speed = Speed.Value;
}
