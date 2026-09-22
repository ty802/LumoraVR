// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Tints each particle from a multi-stop gradient across its lifetime. Stops are two parallel lists:
// Times holds the position of each stop, Colors the colour there. A stop with no matching time is
// ignored rather than guessed at.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorGradient : ParticleModuleBase
{
    // 0 at birth to 1 at death.
    public readonly SyncFieldList<float> Times = new();

    // One per entry in Times.
    public readonly SyncFieldList<colorHDR> Colors = new();

    private readonly ColorGradient _gradient = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    internal override ParticleSimModule CreateSimModule() => new ColorOverLifetimeGradient();

    internal override void PushParameters(ParticleSimModule module)
    {
        ParticleCurveHelper.RebuildGradient(Times, Colors, _gradient, ref _lastRevision);
        ((ColorOverLifetimeGradient)module).Gradient = _gradient;
    }
}
