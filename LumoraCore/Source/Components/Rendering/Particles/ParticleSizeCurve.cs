// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales size from a curve across the lifetime, for envelopes a straight lerp cannot express.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeCurve : ParticleModuleBase
{
    // Key positions, 0 at birth to 1 at death.
    public readonly SyncFieldList<float> Times = new();

    // Size multiplier at each key, one per entry in Times.
    public readonly SyncFieldList<float> Values = new();

    private readonly FloatCurve _curve = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    internal override ParticleSimModule CreateSimModule() => new SizeOverLifetimeCurve();

    internal override void PushParameters(ParticleSimModule module)
    {
        ParticleCurveHelper.Rebuild(Times, Values, _curve, ref _lastRevision);
        ((SizeOverLifetimeCurve)module).Curve = _curve;
    }
}
