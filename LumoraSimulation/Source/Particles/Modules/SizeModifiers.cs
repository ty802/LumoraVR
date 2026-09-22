// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// A flat per-axis scale and offset on the size, clamped.
//
// The dullest module in the set and one of the most used: it is how an effect authored at one scale
// gets reused at another without touching the emitter, and how a sprite sheet drawn square gets
// stretched into a streak. No curve, no speed, no lifetime - just the arithmetic, every frame.
// -xlinka
public sealed class SizeModifier : PositionModuleBase
{
    public float3 Multiplier = float3.One;
    public float3 Offset = float3.Zero;

    public float3 SizeClampMin = float3.Zero;
    public float3 SizeClampMax = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);

        for (int i = 0; i < count; i++)
        {
            var size = source[i] * Multiplier + Offset;
            target[i] = new float3(
                System.Math.Clamp(size.x, SizeClampMin.x, SizeClampMax.x),
                System.Math.Clamp(size.y, SizeClampMin.y, SizeClampMax.y),
                System.Math.Clamp(size.z, SizeClampMin.z, SizeClampMax.z));
        }
    }
}

// Adds to the size in proportion to velocity, per axis.
//
// The additive sibling of SizeMultiplierBySpeed, and they are not interchangeable. A multiplier
// cannot grow a particle that starts at zero and cannot leave a slow particle alone at its authored
// size; an offset does both. It is what stretches a spark along its travel while leaving its cross
// section the width it was drawn.
//
// TWO clamps, applied in order: the offset is clamped before it is added, then the result is clamped
// after. The first stops a single fast particle from growing without bound; the second keeps the
// final size inside whatever the effect can afford. -xlinka
public sealed class SizeOffsetByVelocity : PositionModuleBase
{
    public float3 VelocityMultiplier = float3.Zero;

    public float3 OffsetClampMin = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    public float3 OffsetClampMax = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    public float3 ResultClampMin = float3.Zero;
    public float3 ResultClampMax = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Size;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Size;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var source = SourceSizes.Slice(offset, count);
        var target = Simulation.RenderSizes.Slice(offset, count);

        // No position module means no velocities to read, so the size passes straight through rather
        // than being zeroed by an offset computed from nothing.
        if (PositionModule == null)
        {
            if (source != target)
                source.CopyTo(target);
            return;
        }

        var velocities = PositionModule.Velocities.Slice(offset, count);

        for (int i = 0; i < count; i++)
        {
            var raw = velocities[i] * VelocityMultiplier;
            var clamped = new float3(
                System.Math.Clamp(raw.x, OffsetClampMin.x, OffsetClampMax.x),
                System.Math.Clamp(raw.y, OffsetClampMin.y, OffsetClampMax.y),
                System.Math.Clamp(raw.z, OffsetClampMin.z, OffsetClampMax.z));

            var size = source[i] + clamped;
            target[i] = new float3(
                System.Math.Clamp(size.x, ResultClampMin.x, ResultClampMax.x),
                System.Math.Clamp(size.y, ResultClampMin.y, ResultClampMax.y),
                System.Math.Clamp(size.z, ResultClampMin.z, ResultClampMax.z));
        }
    }
}
