// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailWidthRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinWidth = new();
    public readonly Sync<float> MaxWidth = new();

    public override void OnInit()
    {
        base.OnInit();
        MinWidth.Value = 0.5f;
        MaxWidth.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailWidthRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var width = (TrailWidthRangeInitializer)module;
        width.MinWidth = MinWidth.Value;
        width.MaxWidth = MaxWidth.Value;
    }
}
