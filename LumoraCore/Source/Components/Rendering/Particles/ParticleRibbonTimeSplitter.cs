// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Cuts the ribbon on a time boundary: a fixed cadence, or a gap in emission.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRibbonTimeSplitter : ParticleModuleBase
{
    // Seconds.
    public readonly Sync<float> Interval = new();

    public readonly Sync<RibbonTimeSplitMode> Mode = new();

    public override void OnInit()
    {
        base.OnInit();
        Interval.Value = 0.25f;
    }

    internal override ParticleSimModule CreateSimModule() => new TimeProximityRibbonSplitter();

    internal override void PushParameters(ParticleSimModule module)
    {
        var splitter = (TimeProximityRibbonSplitter)module;
        splitter.Interval = Interval.Value;
        splitter.Mode = Mode.Value;
    }
}
