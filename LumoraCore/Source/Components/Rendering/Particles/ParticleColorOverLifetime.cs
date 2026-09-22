// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Tints each particle from a start colour to an end colour across its lifetime.
//
// Attaching ANY appearance module retires the system's own built-in start/end envelope: the two write
// the same buffers, so leaving both on would mean whichever ran last silently won. The system's
// StartColor/EndColor/StartSize/EndSize fields stop applying the moment one of these is attached.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorOverLifetime : ParticleModuleBase
{
    public readonly Sync<colorHDR> StartColor = new();
    public readonly Sync<colorHDR> EndColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartColor.Value = colorHDR.White;
        EndColor.Value = colorHDR.Transparent;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorOverLifetimeStartEnd)module;
        color.StartColor = StartColor.Value;
        color.EndColor = EndColor.Value;
    }
}
