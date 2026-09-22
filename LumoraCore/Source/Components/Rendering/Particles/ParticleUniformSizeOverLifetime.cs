// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales size by one scalar from a start to an end value across the lifetime.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeOverLifetime : ParticleModuleBase
{
    public readonly Sync<float> StartSize = new();
    public readonly Sync<float> EndSize = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartSize.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (UniformSizeOverLifetimeStartEnd)module;
        size.StartSize = StartSize.Value;
        size.EndSize = EndSize.Value;
    }
}
