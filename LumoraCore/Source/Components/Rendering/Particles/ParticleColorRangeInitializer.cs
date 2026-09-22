// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Multiplies the emitted colour by one drawn per particle between two colours.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorRangeInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    public override void OnInit()
    {
        base.OnInit();
        MinColor.Value = colorHDR.White;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorRangeInitializer)module;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}
