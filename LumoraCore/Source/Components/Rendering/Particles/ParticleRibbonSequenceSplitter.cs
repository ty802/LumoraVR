// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Cuts the ribbon after a run of particles, its length drawn per ribbon from a range. Set both bounds
// the same for exact fixed-length ribbons.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRibbonSequenceSplitter : ParticleModuleBase
{
    public readonly Sync<int> MinCount = new();
    public readonly Sync<int> MaxCount = new();

    public override void OnInit()
    {
        base.OnInit();
        MinCount.Value = 5;
        MaxCount.Value = 10;
    }

    internal override ParticleSimModule CreateSimModule() => new SequenceRibbonSplitter();

    internal override void PushParameters(ParticleSimModule module)
    {
        var splitter = (SequenceRibbonSplitter)module;
        splitter.MinCount = MinCount.Value;
        splitter.MaxCount = MaxCount.Value;
    }
}
