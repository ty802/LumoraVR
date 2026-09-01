// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Scales particles by how fast they are going.
//
// Speed is normalised into MinSpeed..MaxSpeed first, so the curve is authored against 0..1 and stays
// correct when the emitter's speed is retuned - a curve authored directly against metres per second
// silently becomes wrong the moment anything upstream changes.
//
// With no curve the module falls back to a straight lerp from MinMultiplier to MaxMultiplier across
// that same range, so it is useful before anyone has drawn a curve at all. -xlinka
public sealed class SizeMultiplierBySpeed : PositionModuleBase
{
    public FloatCurve? Curve;

    public float MinSpeed;
    public float MaxSpeed = 10f;

    public float MinMultiplier;
    public float MaxMultiplier = 4f;

    public float3 ResultClampMin = float3.Zero;
    public float3 ResultClampMax = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    // Axes the multiplier is applied to. A false axis keeps the incoming size, which is how you get a
    // spark that stretches along its travel without also getting fatter.
    public bool ApplyX = true;
    public bool ApplyY = true;
    public bool ApplyZ = true;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);
        if (PositionModule == null)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }

        var velocities = PositionModule.Velocities.Slice(offset, count);
        float span = MaxSpeed - MinSpeed;
        float inverseSpan = MathF.Abs(span) > 1e-6f ? 1f / span : 0f;
        var curve = Curve;
        bool hasCurve = curve != null && curve.KeyCount > 0;

        for (int i = 0; i < count; i++)
        {
            float t = System.Math.Clamp((velocities[i].Length - MinSpeed) * inverseSpan, 0f, 1f);
            float multiplier = hasCurve
                ? System.Math.Clamp(curve!.Sample(t), MinMultiplier, MaxMultiplier)
                : MinMultiplier + (MaxMultiplier - MinMultiplier) * t;

            var size = source[i];
            var scaled = new float3(
                ApplyX ? size.x * multiplier : size.x,
                ApplyY ? size.y * multiplier : size.y,
                ApplyZ ? size.z * multiplier : size.z);

            target[i] = new float3(
                System.Math.Clamp(scaled.x, ResultClampMin.x, ResultClampMax.x),
                System.Math.Clamp(scaled.y, ResultClampMin.y, ResultClampMax.y),
                System.Math.Clamp(scaled.z, ResultClampMin.z, ResultClampMax.z));
        }
    }
}

// Pulses the size on a sine, per axis.
//
// Phase is offset per particle from the birth seed by default. Without that every particle in a burst
// breathes in perfect unison, which does not read as a crowd of embers - it reads as one object. Turn
// SeedPhase off when the lockstep IS the effect. -xlinka
public sealed class SizeSineMultiplier : ParticleSimModule
{
    public float3 Frequency = float3.One;
    public float3 PhaseOffset = float3.Zero;
    public float3 MinMultiplier = new float3(0.9f, 0.9f, 0.9f);
    public float3 MaxMultiplier = new float3(1.1f, 1.1f, 1.1f);
    public bool SeedPhase = true;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);
        var lifetimes = Simulation.CurrentLifetimes.Slice(offset, count);
        var starting = Simulation.StartingLifetimes.Slice(offset, count);
        var seeds = Simulation.StartingSeeds.Slice(offset, count);

        const float Tau = MathF.PI * 2f;
        bool seeded = SeedPhase;

        for (int i = 0; i < count; i++)
        {
            float age = starting[i] - lifetimes[i];
            float offsetPhase = seeded ? ParticleMath.Seed01(seeds[i]) * Tau : 0f;

            float sx = MathF.Sin(age * Frequency.x * Tau + PhaseOffset.x + offsetPhase) * 0.5f + 0.5f;
            float sy = MathF.Sin(age * Frequency.y * Tau + PhaseOffset.y + offsetPhase) * 0.5f + 0.5f;
            float sz = MathF.Sin(age * Frequency.z * Tau + PhaseOffset.z + offsetPhase) * 0.5f + 0.5f;

            var multiplier = new float3(
                MinMultiplier.x + (MaxMultiplier.x - MinMultiplier.x) * sx,
                MinMultiplier.y + (MaxMultiplier.y - MinMultiplier.y) * sy,
                MinMultiplier.z + (MaxMultiplier.z - MinMultiplier.z) * sz);

            target[i] = source[i] * multiplier;
        }
    }
}
