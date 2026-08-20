// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Points each particle along its own velocity. Below MinimumVelocity the particle keeps whatever
// orientation it had - a stretched spark that has come to rest should not snap to a random facing
// just because its residual velocity is noise. The transition range fades the alignment in over a
// speed band instead of flipping at a threshold. -xlinka
public sealed class OrientByVelocity : PositionModuleBase
{
    public float3 Up = float3.Up;

    // Below this speed the particle is left alone.
    public float MinimumVelocity;

    // Speed band above the minimum over which the alignment fades in. 0 snaps.
    public float VelocityTransitionRange;

    public override ParticleSimPhase Phase => ParticleSimPhase.Orientation;
    public override bool AllowMultipleInstances => false;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Rotation;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Rotation;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var velocities = PositionModule.Velocities.Slice(offset, count);
        var source = SourceRotations.Slice(offset, count);
        var target = Simulation.RenderRotations.Slice(offset, count);
        float range = VelocityTransitionRange;
        float inverseRange = range > 1e-6f ? 1f / range : 0f;

        for (int i = 0; i < count; i++)
        {
            var velocity = velocities[i];
            float speed = velocity.Length;
            if (speed < MinimumVelocity || speed < 1e-6f)
            {
                target[i] = source[i];
                continue;
            }
            var aligned = ParticleMath.FromForwardUp(velocity / speed, Up) * source[i];
            float weight = inverseRange > 0f ? (speed - MinimumVelocity) * inverseRange : 1f;
            target[i] = weight >= 1f ? aligned : floatQ.Slerp(source[i], aligned, System.Math.Clamp(weight, 0f, 1f));
        }
    }
}

// Points each particle at a fixed point in simulation space. Sparks facing a flame core.
public sealed class OrientAtPoint : ParticleSimModule
{
    public float3 TargetPoint;
    public float3 Up = float3.Up;

    public override ParticleSimPhase Phase => ParticleSimPhase.Orientation;
    public override bool AllowMultipleInstances => false;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Position | ParticleRenderChannel.Rotation;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Rotation;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var positions = Simulation.RenderPositions.Slice(offset, count);
        var source = SourceRotations.Slice(offset, count);
        var target = Simulation.RenderRotations.Slice(offset, count);
        for (int i = 0; i < count; i++)
            target[i] = ParticleMath.FromForwardUp(TargetPoint - positions[i], Up) * source[i];
    }
}
