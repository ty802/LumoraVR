// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Nominates particles as real light sources.
//
// This component does not create lights. It publishes a short, ranked, hard-capped list of candidates
// each frame and leaves the renderer to spend its light budget on them, because the budget depends on
// what is on screen and what was already lit last frame - neither of which the simulation can see.
//
// Rank is the particle's intensity weighted by where it is in its life, so a light that has just been
// born or is about to die is the one dropped when the budget runs out: the drop nobody notices. Keep
// MaxLights small. Every candidate the renderer promotes is a real dynamic light with a real shadow
// cost, and forty of them in a fountain will cost more than the fountain. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLights : ParticleModuleBase
{
    [Group("Budget")]
    // Hard cap on published candidates.
    public readonly Sync<int> MaxLights = new();

    public readonly Sync<ParticleLightSelection> Selection = new();

    // EveryNth stride. 1 considers every particle.
    public readonly Sync<int> Stride = new();

    [Group("Light")]
    public readonly Sync<colorHDR> BaseColor = new();
    public readonly Sync<float> BaseIntensity = new();

    // Metres.
    public readonly Sync<float> BaseRange = new();

    [Group("From Particle")]
    public readonly Sync<bool> ColorFromParticle = new();

    // Folds the particle's alpha into the intensity, so a light fades out with its particle.
    public readonly Sync<bool> IntensityFromAlpha = new();

    public readonly Sync<bool> RangeFromSize = new();
    public readonly Sync<ParticleSizeAxis> RangeAxis = new();

    [Group("Rank")]
    // Fraction of the lifetime over which a light's rank rises at birth.
    public readonly Sync<float> RiseFraction = new();

    // Fraction over which it falls again before death.
    public readonly Sync<float> FallFraction = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxLights.Value = 8;
        Selection.Value = ParticleLightSelection.EveryNth;
        Stride.Value = 8;
        BaseColor.Value = colorHDR.White;
        BaseIntensity.Value = 1f;
        BaseRange.Value = 4f;
        ColorFromParticle.Value = true;
        IntensityFromAlpha.Value = true;
        RangeFromSize.Value = true;
        RangeAxis.Value = ParticleSizeAxis.Average;
        RiseFraction.Value = 0.1f;
        FallFraction.Value = 0.25f;
    }

    internal override ParticleSimModule CreateSimModule() => new ParticleLightsModule();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lights = (ParticleLightsModule)module;
        lights.MaxLights = MaxLights.Value;
        lights.Selection = Selection.Value;
        lights.Stride = Stride.Value;
        lights.BaseColor = BaseColor.Value;
        lights.BaseIntensity = BaseIntensity.Value;
        lights.BaseRange = BaseRange.Value;
        lights.ColorFromParticle = ColorFromParticle.Value;
        lights.IntensityFromAlpha = IntensityFromAlpha.Value;
        lights.RangeFromSize = RangeFromSize.Value;
        lights.RangeAxis = RangeAxis.Value;
        lights.RiseFraction = RiseFraction.Value;
        lights.FallFraction = FallFraction.Value;
    }
}
