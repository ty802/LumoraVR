// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emitted size by one fixed scalar on every axis.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeInitializer : ParticleModuleBase
{
    public readonly Sync<float> Size = new();

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((UniformSizeConstantInitializer)module).Size = Size.Value;
}
