// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Multiplies the emitted colour by a fixed colour.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> Color = new();

    public override void OnInit()
    {
        base.OnInit();
        Color.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((ColorConstantInitializer)module).Color = Color.Value;
}
