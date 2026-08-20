// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

public sealed class ColorOverLifetimeStartEnd : ParticleSimModule
{
    public colorHDR StartColor = colorHDR.White;
    public colorHDR EndColor = colorHDR.Transparent;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceColors.Slice(offset, count);
        var target = Simulation.RenderColors.Slice(offset, count);
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
            target[i] = source[i] * colorHDR.Lerp(StartColor, EndColor, progression[i]);
    }
}

public sealed class ColorOverLifetimeGradient : ParticleSimModule
{
    public ColorGradient? Gradient;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceColors.Slice(offset, count);
        var target = Simulation.RenderColors.Slice(offset, count);
        var gradient = Gradient;
        if (gradient == null || gradient.KeyCount == 0)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
            target[i] = source[i] * gradient.Sample(progression[i]);
    }
}

// Scales alpha from a curve across the lifetime, leaving the colour alone.
public sealed class AlphaOverLifetimeCurve : ParticleSimModule
{
    public FloatCurve? Curve;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceColors.Slice(offset, count);
        var target = Simulation.RenderColors.Slice(offset, count);
        var curve = Curve;
        if (curve == null || curve.KeyCount == 0)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
        {
            var c = source[i];
            c.a *= curve.Sample(progression[i]);
            target[i] = c;
        }
    }
}

// Scales each axis of the size from a start to an end value across the lifetime.
public sealed class SizeOverLifetimeStartEnd : ParticleSimModule
{
    public float3 StartSize = float3.One;
    public float3 EndSize = float3.Zero;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
            target[i] = source[i] * float3.Lerp(StartSize, EndSize, progression[i]);
    }
}

// Same as SizeOverLifetimeStartEnd with one scalar for all three axes.
public sealed class UniformSizeOverLifetimeStartEnd : ParticleSimModule
{
    public float StartSize = 1f;
    public float EndSize;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
        {
            float t = progression[i];
            target[i] = source[i] * (StartSize + (EndSize - StartSize) * t);
        }
    }
}

// Scales size from a curve across the lifetime, for envelopes a straight lerp cannot express.
public sealed class SizeOverLifetimeCurve : ParticleSimModule
{
    public FloatCurve? Curve;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);
        var curve = Curve;
        if (curve == null || curve.KeyCount == 0)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        for (int i = 0; i < count; i++)
            target[i] = source[i] * curve.Sample(progression[i]);
    }
}

public sealed class ColorBySpeed : PositionModuleBase
{
    public float MinSpeed;
    public float MaxSpeed = 1f;
    public colorHDR MinColor = colorHDR.Black;
    public colorHDR MaxColor = colorHDR.White;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceColors.Slice(offset, count);
        var target = Simulation.RenderColors.Slice(offset, count);
        if (PositionModule == null)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }
        var velocities = PositionModule.Velocities.Slice(offset, count);
        float span = MaxSpeed - MinSpeed;
        float inverseSpan = MathF.Abs(span) > 1e-6f ? 1f / span : 0f;
        for (int i = 0; i < count; i++)
        {
            float t = System.Math.Clamp((velocities[i].Length - MinSpeed) * inverseSpan, 0f, 1f);
            target[i] = source[i] * colorHDR.Lerp(MinColor, MaxColor, t);
        }
    }
}

// Tints particles by how their travel direction lines up with a reference direction: one colour when
// they head straight along it, another at right angles, a third when they come back the other way.
public sealed class ColorByVelocityDirection : PositionModuleBase
{
    public float3 ReferenceDirection = float3.Forward;
    public colorHDR AlignedColor = colorHDR.White;
    public colorHDR OrthogonalColor = colorHDR.White;
    public colorHDR OppositeColor = colorHDR.White;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceColors.Slice(offset, count);
        var target = Simulation.RenderColors.Slice(offset, count);
        if (PositionModule == null)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }
        var velocities = PositionModule.Velocities.Slice(offset, count);
        var reference = ReferenceDirection.Normalized;
        for (int i = 0; i < count; i++)
        {
            var velocity = velocities[i];
            float speed = velocity.Length;
            float alignment = speed > 1e-6f ? float3.Dot(velocity / speed, reference) : 0f;
            var tint = alignment >= 0f
                ? colorHDR.Lerp(OrthogonalColor, AlignedColor, alignment)
                : colorHDR.Lerp(OrthogonalColor, OppositeColor, -alignment);
            target[i] = source[i] * tint;
        }
    }
}

// The classic spark/spray envelope in one module: start-to-end size and colour, a fade in at birth
// and out at death so nothing pops into or out of existence, a brief size overshoot early in life,
// and a per-particle size jitter so a burst does not look like a set of identical clones.
//
// The jitter is derived from the particle's birth seed rather than drawn fresh, so it is stable for
// the particle's whole life and identical on every peer running the same system seed. -xlinka
public sealed class SizeColorEnvelope : ParticleSimModule
{
    public float StartSize = 1f;
    public float EndSize = 1f;
    public colorHDR StartColor = colorHDR.White;
    public colorHDR EndColor = colorHDR.Transparent;

    // Per-particle size spread as a fraction, so 0.22 means each particle is 0.78x to 1.22x.
    public float SizeJitter = 0.22f;

    // Fraction of the lifetime spent fading in at birth.
    public float FadeIn = 0.16f;

    // Point in the lifetime where the fade out begins.
    public float FadeOutStart = 0.74f;

    // Extra size at the peak of the birth overshoot. 0 disables the pop.
    public float PopStrength = 0.48f;

    // How much of the lifetime the overshoot covers.
    public float PopDuration = 0.22f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size | ParticleRenderChannel.Color;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size | ParticleRenderChannel.Color;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var sourceSizes = SourceSizes.Slice(offset, count);
        var sourceColors = SourceColors.Slice(offset, count);
        var targetSizes = Simulation.RenderSizes.Slice(offset, count);
        var targetColors = Simulation.RenderColors.Slice(offset, count);
        var progression = Simulation.NormalizedProgressions.Slice(offset, count);
        var seeds = Simulation.StartingSeeds.Slice(offset, count);

        float jitterBase = 1f - SizeJitter;
        float jitterSpan = SizeJitter * 2f;
        float popDuration = MathF.Max(PopDuration, 1e-4f);

        for (int i = 0; i < count; i++)
        {
            float t = System.Math.Clamp(progression[i], 0f, 1f);
            float birth = ParticleMath.SmoothStep(0f, FadeIn, t);
            float death = 1f - ParticleMath.SmoothStep(FadeOutStart, 1f, t);
            float pop = 1f + MathF.Sin(System.Math.Clamp(t / popDuration, 0f, 1f) * MathF.PI) * PopStrength;
            float jitter = jitterBase + ParticleMath.Seed01(seeds[i]) * jitterSpan;

            float size = (StartSize + (EndSize - StartSize) * t) * jitter * birth * death * pop;
            targetSizes[i] = sourceSizes[i] * size;

            var tint = colorHDR.Lerp(StartColor, EndColor, t);
            tint.a *= birth * death;
            targetColors[i] = sourceColors[i] * tint;
        }
    }
}
