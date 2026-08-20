// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Emitters;

// A flat XZ disc that throws particles upward with a horizontal scatter. Unlike the general shape
// emitters this one writes the FINAL velocity rather than a direction to be scaled later: the upward
// component is the speed and the horizontal components are the scatter, in units per second. That is
// deliberate - it is what makes the spray widen with distance travelled instead of with speed, and it
// is the shape a plain "sparks off a surface" effect wants without any initializer stack at all.
//
// Because it writes velocity directly it also writes lifetime, so it stands alone as the whole
// emission side of a simple system. -xlinka
public sealed class DiscSprayEmitter : ParticleSimEmitter
{
    // Disc radius on X and Z. Y is ignored.
    public float3 Extents = new float3(1f, 0f, 1f);

    public float SpawnHeight;

    // Upward speed, before variance.
    public float Speed = 1f;

    public float SpeedVariance;

    // Horizontal scatter in units per second, symmetric on X and Z.
    public float Spread;

    public float Lifetime = 1f;

    public float LifetimeVariance;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => true;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var extents = Extents;
        float spread = Spread;

        for (int i = 0; i < count; i++)
        {
            float angle = random.Value * MathF.PI * 2f;
            // sqrt of a uniform draw keeps the disc evenly covered instead of clumping at the centre.
            float radius = MathF.Sqrt(random.Value);
            positions[i] = new float3(
                MathF.Cos(angle) * extents.x * radius,
                SpawnHeight,
                MathF.Sin(angle) * extents.z * radius);

            float speed = MathF.Max(0.02f, Speed + random.Signed * SpeedVariance);
            directions[i] = new float3(random.Signed * spread, speed, random.Signed * spread);
            lifetimes[i] = MathF.Max(0.05f, Lifetime + random.Signed * LifetimeVariance);
        }
        return count;
    }
}
