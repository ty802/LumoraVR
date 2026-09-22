// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Tints particles by how their travel direction lines up with a reference direction: one colour
// heading along it, another at right angles, a third coming back the other way.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorByDirection : ParticleModuleBase
{
    // In the system's simulation space.
    public readonly Sync<float3> ReferenceDirection = new();

    public readonly Sync<colorHDR> AlignedColor = new();
    public readonly Sync<colorHDR> OrthogonalColor = new();
    public readonly Sync<colorHDR> OppositeColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        ReferenceDirection.Value = float3.Forward;
        AlignedColor.Value = colorHDR.White;
        OrthogonalColor.Value = colorHDR.White;
        OppositeColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorByVelocityDirection();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorByVelocityDirection)module;
        color.ReferenceDirection = ReferenceDirection.Value;
        color.AlignedColor = AlignedColor.Value;
        color.OrthogonalColor = OrthogonalColor.Value;
        color.OppositeColor = OppositeColor.Value;
    }
}
