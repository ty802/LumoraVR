// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emitted size from a per-axis range. Uniform draws ONE number and applies it to all three
// axes, keeping particles proportional; turn it off to draw per axis and get squashed shapes.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinSize = new();
    public readonly Sync<float3> MaxSize = new();
    public readonly Sync<bool> Uniform = new();

    public override void OnInit()
    {
        base.OnInit();
        MinSize.Value = float3.One;
        MaxSize.Value = float3.One;
        Uniform.Value = true;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (SizeRangeInitializer)module;
        size.MinSize = MinSize.Value;
        size.MaxSize = MaxSize.Value;
        size.Uniform = Uniform.Value;
    }
}
