// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Emitters;

// How an authored direction is carried from the emitter shape's space into simulation space. It
// matters because the magnitude means something here - the emission vector becomes the initial
// velocity - so a scaled emitter can either speed its particles up with it or not, and that has to
// be a choice rather than an accident. -xlinka
public enum DirectionTransformMode
{
    // Full transform including scale, so a scaled emitter emits proportionally faster.
    AsVector,

    // Rotate only, keeping the authored magnitude.
    AsDirectionWithOriginalMagnitude,

    // Rotate and normalize, so the magnitude is always 1.
    AsUnitDirection,

    // Already in simulation space, left alone.
    AsTargetSpaceVector,
}

public enum BoxEmitterDirection
{
    // One authored direction for every particle.
    Fixed,

    ClosestFaceNormal,
}

public enum SphereEmitterDirection
{
    // Straight out from the centre, unit length.
    RadialUniform,

    // Out from the centre, faster the further out the particle spawned.
    RadialProportional,

    // One authored direction for every particle.
    Forced,
}

public enum ConeEmitterDirection
{
    Fixed,

    // Out along the cone's flare, which is what makes a cone read as a cone.
    RadialUniform,
}

public enum CircleEmitterAlignment
{
    XY,
    XZ,
    YZ,
}

public enum CircleEmitterDirection
{
    RadialUniform,
    RadialProportional,
    Fixed,

    // Fixed direction, scaled by how far out the particle spawned.
    FixedProportional,

    // Fixed direction, scaled by how far IN the particle spawned.
    FixedReversedProportional,
}

public enum CylinderEmitterDirection
{
    Fixed,

    // Straight out from the axis, ignoring height.
    CircleUniform,

    // Out from the centre of the cylinder, so the caps splay.
    RadialUniform,
}

public enum CylinderEmitterCapsDirection
{
    Fixed,
    RadialUniform,

    // Along the cap's own normal, so the ends puff outward.
    Facing,
}

public enum LineEmitterDirection
{
    // Relative to the line's own axis, so the spray follows a bent line.
    LineAligned,

    Fixed,
}

public enum MeshEmissionSource
{
    Vertices,
    Edges,
    Faces,
}

public enum MeshEmitterDirection
{
    // One direction in mesh space for every particle.
    LocalSpace,

    // Relative to the surface at the sample point, so +Z means "along the normal".
    TangentSpace,
}

// Shared direction transform used by every shape emitter, so a mode means one thing.
public static class DirectionTransformHelper
{
    public static float3 Transform(in float4x4 trs, in float3 direction, DirectionTransformMode mode)
    {
        switch (mode)
        {
            case DirectionTransformMode.AsVector:
                return trs.MultiplyVector(direction);
            case DirectionTransformMode.AsDirectionWithOriginalMagnitude:
            {
                float magnitude = direction.Length;
                var rotated = trs.MultiplyVector(direction);
                float len = rotated.Length;
                return len > 1e-6f ? rotated * (magnitude / len) : rotated;
            }
            case DirectionTransformMode.AsUnitDirection:
                return trs.MultiplyVector(direction).Normalized;
            case DirectionTransformMode.AsTargetSpaceVector:
                return direction;
            default:
                return float3.Zero;
        }
    }

    // Blend every direction toward a random one by weight. A weight of 1 is fully random, and it is
    // handled as an early-out rather than a blend so "fully random" is exactly that instead of a
    // slerp that still remembers the original direction at the poles.
    public static void ApplyRandomSpread(Span<float3> directions, SeededRandom random, float weight)
    {
        if (weight <= 0f)
            return;
        if (weight >= 1f)
        {
            for (int i = 0; i < directions.Length; i++)
                directions[i] = random.OnUnitSphere * directions[i].Length;
            return;
        }
        for (int i = 0; i < directions.Length; i++)
            directions[i] = ParticleMath.BlendDirection(directions[i], random.OnUnitSphere, weight);
    }
}
