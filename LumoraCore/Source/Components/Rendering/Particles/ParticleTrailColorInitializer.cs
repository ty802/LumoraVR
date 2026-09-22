// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailColorInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> Color = new();

    public override void OnInit()
    {
        base.OnInit();
        Color.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailColorConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((TrailColorConstantInitializer)module).Color = Color.Value;
}
