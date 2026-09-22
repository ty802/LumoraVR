// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales the emitted size by one scalar per particle, drawn from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinSize = new();
    public readonly Sync<float> MaxSize = new();

    public override void OnInit()
    {
        base.OnInit();
        MinSize.Value = 1f;
        MaxSize.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (UniformSizeRangeInitializer)module;
        size.MinSize = MinSize.Value;
        size.MaxSize = MaxSize.Value;
    }
}
