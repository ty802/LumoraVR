// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Scales particles by how fast they are going: sparks that stretch when they fly and shrink when they
// settle, debris that grows on the way out of an explosion.
//
// Speed is normalised into MinSpeed..MaxSpeed before the curve is sampled, so the curve is authored
// against 0 to 1 and survives a retune of the emitter's speed. Leave the curve empty for a straight
// lerp from MinMultiplier to MaxMultiplier.
//
// Switching off an axis leaves the incoming size on it, which is how a spark stretches along its
// travel without also getting fatter.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeBySpeed : ParticleModuleBase
{
    [Group("Speed Range")]
    public readonly Sync<float> MinSpeed = new();
    public readonly Sync<float> MaxSpeed = new();

    [Group("Multiplier")]
    public readonly Sync<float> MinMultiplier = new();
    public readonly Sync<float> MaxMultiplier = new();

    // Key positions, 0 at MinSpeed to 1 at MaxSpeed.
    public readonly SyncFieldList<float> Times = new();

    // Size multiplier at each key, one per entry in Times.
    public readonly SyncFieldList<float> Values = new();

    [Group("Axes")]
    public readonly Sync<bool> ApplyX = new();
    public readonly Sync<bool> ApplyY = new();
    public readonly Sync<bool> ApplyZ = new();

    [Group("Clamp")]
    public readonly Sync<float3> ResultClampMin = new();
    public readonly Sync<float3> ResultClampMax = new();

    private readonly FloatCurve _curve = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        MaxSpeed.Value = 10f;
        MaxMultiplier.Value = 4f;
        ApplyX.Value = true;
        ApplyY.Value = true;
        ApplyZ.Value = true;
        ResultClampMin.Value = float3.Zero;
        ResultClampMax.Value = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    }

    internal override ParticleSimModule CreateSimModule() => new SizeMultiplierBySpeed();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (SizeMultiplierBySpeed)module;
        size.MinSpeed = MinSpeed.Value;
        size.MaxSpeed = MaxSpeed.Value;
        size.MinMultiplier = MinMultiplier.Value;
        size.MaxMultiplier = MaxMultiplier.Value;
        size.ApplyX = ApplyX.Value;
        size.ApplyY = ApplyY.Value;
        size.ApplyZ = ApplyZ.Value;
        size.ResultClampMin = ResultClampMin.Value;
        size.ResultClampMax = ResultClampMax.Value;

        ParticleCurveHelper.Rebuild(Times, Values, _curve, ref _lastRevision);
        size.Curve = _curve;
    }
}

// Pulses the size on a sine, per axis. Embers breathing, a shield shimmering, anything that should not
// sit perfectly still.
//
// SeedPhase offsets each particle's phase from its birth seed. With it off, every particle in a burst
// breathes in unison and the whole thing reads as one object rather than a crowd.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeSine : ParticleModuleBase
{
    // Cycles per second, per axis.
    public readonly Sync<float3> Frequency = new();

    // Radians, per axis.
    public readonly Sync<float3> PhaseOffset = new();

    public readonly Sync<float3> MinMultiplier = new();
    public readonly Sync<float3> MaxMultiplier = new();

    public readonly Sync<bool> SeedPhase = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        Frequency.Value = float3.One;
        MinMultiplier.Value = new float3(0.9f, 0.9f, 0.9f);
        MaxMultiplier.Value = new float3(1.1f, 1.1f, 1.1f);
        SeedPhase.Value = true;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeSineMultiplier();

    internal override void PushParameters(ParticleSimModule module)
    {
        var sine = (SizeSineMultiplier)module;
        sine.Frequency = Frequency.Value;
        sine.PhaseOffset = PhaseOffset.Value;
        sine.MinMultiplier = MinMultiplier.Value;
        sine.MaxMultiplier = MaxMultiplier.Value;
        sine.SeedPhase = SeedPhase.Value;
    }
}
