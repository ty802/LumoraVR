// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// A flat per-axis scale and offset on particle size.
//
// Nothing clever, and that is the point: it is how an effect authored at one scale is reused at
// another without retuning the emitter, and how a square sprite is stretched into a streak. Applied
// every frame, after whatever set the size in the first place.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeModifier : ParticleModuleBase
{
    [Group("Size")]
    public readonly Sync<float3> Multiplier = new();
    public readonly Sync<float3> Offset = new();

    [Group("Clamp")]
    public readonly Sync<float3> SizeClampMin = new();
    public readonly Sync<float3> SizeClampMax = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        Multiplier.Value = float3.One;
        Offset.Value = float3.Zero;
        SizeClampMin.Value = float3.Zero;
        SizeClampMax.Value = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    }

    internal override ParticleSimModule CreateSimModule() => new Simulation.Particles.Modules.SizeModifier();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (Simulation.Particles.Modules.SizeModifier)module;
        size.Multiplier = Multiplier.Value;
        size.Offset = Offset.Value;
        size.SizeClampMin = SizeClampMin.Value;
        size.SizeClampMax = SizeClampMax.Value;
    }
}
