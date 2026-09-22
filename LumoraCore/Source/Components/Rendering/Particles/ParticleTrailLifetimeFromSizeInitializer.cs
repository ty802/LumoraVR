// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Ties trail length to particle size, so a burst of mixed sizes gets proportionate streaks instead of
// every particle dragging the same tail.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeFromSizeInitializer : ParticleModuleBase
{
    // Particle size that maps to a full-length trail.
    public readonly Sync<float> ReferenceSize = new();

    public readonly Sync<ParticleSizeAxis> Axis = new();

    public override void OnInit()
    {
        base.OnInit();
        ReferenceSize.Value = 1f;
        Axis.Value = ParticleSizeAxis.Average;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeFromSizeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (TrailLifetimeFromSizeInitializer)module;
        lifetime.ReferenceSize = ReferenceSize.Value;
        lifetime.Axis = Axis.Value;
    }
}
