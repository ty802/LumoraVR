// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emitted size by a fixed per-axis value.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> Size = new();

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = float3.One;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((SizeConstantInitializer)module).Size = Size.Value;
}
