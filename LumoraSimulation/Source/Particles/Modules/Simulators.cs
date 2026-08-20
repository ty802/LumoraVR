// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Owns the velocity column and turns it into movement. Forces write velocity, this consumes it -
// which is why it sits in the Integrate phase, after every Force-phase module has had its say on the
// same frame's velocity (semi-implicit Euler). Without it a system has no motion at all: no other
// module owns a velocity buffer, so forces have nothing to write into.
//
// With a raycaster attached it also does swept collision: probe along this frame's movement, and on a
// hit reflect the velocity, scale it by the bounce ratio, and park the particle just off the surface.
// The small offsets either side of the probe exist because starting a ray exactly on a surface is a
// coin flip on whether it self-hits. -xlinka
public sealed class PositionSimulatorModule : ParticleSimModule
{
    // Ray start/end padding. Big enough to clear surface self-hits, small enough to be invisible.
    private const float RaycastTolerance = 1e-4f;

    private readonly ParticleBuffer<float3> _velocities = new();

    // Collision back end, or null for particles that pass through everything.
    public IParticleCollisionRaycaster? CollisionRaycaster;

    // How much speed survives a bounce. 0 sticks, 1 is perfectly elastic.
    public float CollisionBounceRatio;

    // Fraction of the particle's total lifetime burned per collision. 1 kills on contact.
    public float CollisionLifetimeLossRatio;

    public override ParticleSimPhase Phase => ParticleSimPhase.Integrate;

    public override bool AllowMultipleInstances => false;

    public override ParticleRenderChannel Produces => ParticleRenderChannel.Position;

    // Runs first in the newborn pass so speed initializers have a velocity column to write.
    public override int InitPriority => -100;

    // The live velocity column. Force modules write here.
    public Span<float3> Velocities => _velocities.AsSpan();

    public override void InitializeNewParticles(int index, int count)
    {
        var target = _velocities.IncreaseCount(count);
        Simulation.StartingDirections.Slice(index, count).CopyTo(target);
    }

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var positions = Simulation.RenderPositions.Slice(offset, count);
        var velocities = _velocities.Slice(offset, count);
        var raycaster = CollisionRaycaster;

        if (raycaster == null)
        {
            for (int i = 0; i < count; i++)
                positions[i] += velocities[i] * deltaTime;
            return;
        }

        var lifetimes = Simulation.CurrentLifetimes.Slice(offset, count);
        var startingLifetimes = Simulation.StartingLifetimes.Slice(offset, count);
        float bounce = CollisionBounceRatio;
        float lifetimeLoss = CollisionLifetimeLossRatio;

        for (int i = 0; i < count; i++)
        {
            var step = velocities[i] * deltaTime;
            float distance = step.Length;
            if (distance < 1e-8f)
                continue;
            var direction = step / distance;

            var hit = raycaster.RaycastParticle(
                positions[i] - direction * RaycastTolerance,
                direction,
                distance + RaycastTolerance * 2f);

            if (!hit.IsHit)
            {
                positions[i] += step;
                continue;
            }

            float travelled = hit.Distance - RaycastTolerance;
            if (travelled < 0f)
            {
                // Started inside or right on the surface. Leaving is fine; entering is not.
                if (float3.Dot(direction, hit.Normal) > -0.01f)
                {
                    positions[i] += step;
                    continue;
                }
                travelled = 0f;
            }

            var reflected = float3.Reflect(direction, hit.Normal);
            velocities[i] = reflected * (velocities[i].Length * bounce);
            float remaining = MathF.Max((distance - travelled) * bounce, RaycastTolerance);
            positions[i] = hit.Point + hit.Normal * RaycastTolerance + reflected * remaining;

            if (lifetimeLoss > 0f)
                lifetimes[i] -= lifetimeLoss * startingLifetimes[i];
        }
    }

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
        => _velocities.ApplyCompaction(moves, newCount);

    public override void TrimParticles(int newCount) => _velocities.TrimParticles(newCount);
}

// Owns the angular velocity column and integrates it into the render rotation. Same relationship to
// the angular forces that PositionSimulatorModule has to the linear ones.
public sealed class RotationSimulatorModule : ParticleSimModule
{
    private readonly ParticleBuffer<float3> _angularVelocities = new();
    private readonly ParticleBuffer<floatQ> _rotations = new();

    public override ParticleSimPhase Phase => ParticleSimPhase.Integrate;

    public override bool AllowMultipleInstances => false;

    public override ParticleRenderChannel Produces => ParticleRenderChannel.Rotation;

    public override int InitPriority => -100;

    // Radians per second per axis. Angular force modules write here.
    public Span<float3> AngularVelocities => _angularVelocities.AsSpan();

    public override void InitializeNewParticles(int index, int count)
        => _angularVelocities.IncreaseCount(count).Fill(float3.Zero);

    public override void NewParticlesInitialized(int index, int count)
    {
        var target = _rotations.IncreaseCount(count);
        Simulation.StartingRotations.Slice(index, count).CopyTo(target);
    }

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var angular = _angularVelocities.Slice(offset, count);
        var rotations = _rotations.Slice(offset, count);
        var render = Simulation.RenderRotations.Slice(offset, count);

        for (int i = 0; i < count; i++)
        {
            var velocity = angular[i];
            float speed = velocity.Length;
            if (speed > 1e-6f)
            {
                var delta = floatQ.AxisAngleRad(velocity / speed, speed * deltaTime);
                rotations[i] = (rotations[i] * delta).Normalized;
            }
            render[i] = rotations[i];
        }
    }

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        _angularVelocities.ApplyCompaction(moves, newCount);
        _rotations.ApplyCompaction(moves, newCount);
    }

    public override void TrimParticles(int newCount)
    {
        _angularVelocities.TrimParticles(newCount);
        _rotations.TrimParticles(newCount);
    }
}

// Base for modules that need the velocity column the position integrator owns.
public abstract class PositionModuleBase : ParticleSimModule
{
    protected PositionSimulatorModule? PositionModule { get; private set; }

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        PositionModule = Simulation.TryGetModule<PositionSimulatorModule>();
    }
}

// Base for modules that need the angular velocity column the rotation integrator owns.
public abstract class RotationModuleBase : ParticleSimModule
{
    protected RotationSimulatorModule? RotationModule { get; private set; }

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        RotationModule = Simulation.TryGetModule<RotationSimulatorModule>();
    }
}
