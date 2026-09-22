// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Multiplies the emitted lifetime by a value drawn per particle from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLifetimeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinLifetime = new();
    public readonly Sync<float> MaxLifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        MinLifetime.Value = 1f;
        MaxLifetime.Value = 2f;
    }

    internal override ParticleSimModule CreateSimModule() => new LifetimeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (LifetimeRangeInitializer)module;
        lifetime.MinLifetime = MinLifetime.Value;
        lifetime.MaxLifetime = MaxLifetime.Value;
    }
}
