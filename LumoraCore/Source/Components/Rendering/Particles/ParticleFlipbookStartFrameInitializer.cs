// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scatters the starting frame across a range, for sheets where only part of the grid is a sensible
// place to begin. Overrides the flipbook's own StartFrame for every particle it touches.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleFlipbookStartFrameInitializer : ParticleModuleBase
{
    public readonly Sync<int> MinFrame = new();
    public readonly Sync<int> MaxFrame = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxFrame.Value = 1;
    }

    internal override ParticleSimModule CreateSimModule() => new FlipbookStartFrameInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var start = (FlipbookStartFrameInitializer)module;
        start.MinFrame = MinFrame.Value;
        start.MaxFrame = MaxFrame.Value;
    }
}
