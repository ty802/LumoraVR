// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinLifetime = new();
    public readonly Sync<float> MaxLifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        MinLifetime.Value = 0.5f;
        MaxLifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (TrailLifetimeRangeInitializer)module;
        lifetime.MinLifetime = MinLifetime.Value;
        lifetime.MaxLifetime = MaxLifetime.Value;
    }
}
