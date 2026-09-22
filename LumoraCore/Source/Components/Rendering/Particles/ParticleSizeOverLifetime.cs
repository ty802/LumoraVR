// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales each axis of the particle size from a start to an end value across its lifetime.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeOverLifetime : ParticleModuleBase
{
    public readonly Sync<float3> StartSize = new();
    public readonly Sync<float3> EndSize = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartSize.Value = float3.One;
        EndSize.Value = float3.Zero;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (SizeOverLifetimeStartEnd)module;
        size.StartSize = StartSize.Value;
        size.EndSize = EndSize.Value;
    }
}
