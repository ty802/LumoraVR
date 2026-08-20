// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// Result of a particle collision probe, in simulation-local space.
public readonly struct ParticleRayHit
{
    public readonly float3 Point;
    public readonly float3 Normal;

    // Distance along the probe, negative when nothing was struck.
    public readonly float Distance;

    public static readonly ParticleRayHit NoHit = new(float3.Zero, float3.Up, -1f);

    public ParticleRayHit(in float3 point, in float3 normal, float distance)
    {
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    public bool IsHit => Distance >= 0f;
}

// Collision back end for particles. The simulation has no idea what world it is running in, so the
// host supplies this; origin, direction and the returned hit are all in SIMULATION-LOCAL space, and
// converting to and from whatever the host's physics uses is the host's problem.
public interface IParticleCollisionRaycaster
{
    ParticleRayHit RaycastParticle(in float3 origin, in float3 direction, float distance);
}
