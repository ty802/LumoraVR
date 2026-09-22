// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Tints particles by how fast they are moving, between a slow colour and a fast colour.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorBySpeed : ParticleModuleBase
{
    public readonly Sync<float> MinSpeed = new();
    public readonly Sync<float> MaxSpeed = new();
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        MaxSpeed.Value = 1f;
        MinColor.Value = colorHDR.Black;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorBySpeed();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorBySpeed)module;
        color.MinSpeed = MinSpeed.Value;
        color.MaxSpeed = MaxSpeed.Value;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}
