// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailColorRangeInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    public override void OnInit()
    {
        base.OnInit();
        MinColor.Value = colorHDR.White;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailColorRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (TrailColorRangeInitializer)module;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}
