// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales how long ONE trail's points survive, against the trail module's MaxPointAge.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeInitializer : ParticleModuleBase
{
    public readonly Sync<float> Lifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        Lifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((TrailLifetimeConstantInitializer)module).Lifetime = Lifetime.Value;
}
