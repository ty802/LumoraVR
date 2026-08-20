// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// How a force combines with the velocity it is applied to.
public enum ParticleForceMode
{
    // Classic acceleration: velocity += force * strength * dt.
    Additive,

    // Steer without accelerating: direction bends, speed is preserved.
    MaintainSpeed,

    // Turn toward the force direction at a rate set by strength, keeping speed.
    AlterDirection,

    OverrideVelocity,

    // Keep the current speed, take the force's direction.
    OverrideDirection,
}

// Shared force-combination math, so every force module means the same thing by a mode.
public static class ParticleForceHelper
{
    public static void Apply(ref float3 velocity, ParticleForceMode mode, in float3 force, float strength, float deltaTime)
    {
        switch (mode)
        {
            case ParticleForceMode.Additive:
                velocity += force * (strength * deltaTime);
                break;
            case ParticleForceMode.MaintainSpeed:
            {
                float speed = velocity.Length;
                var steered = velocity + force * (strength * deltaTime);
                float len = steered.Length;
                velocity = len > 1e-6f ? steered * (speed / len) : velocity;
                break;
            }
            case ParticleForceMode.AlterDirection:
            {
                float speed = velocity.Length;
                if (speed < 1e-6f)
                    break;
                float forceLen = force.Length;
                if (forceLen < 1e-6f)
                    break;
                float t = System.Math.Clamp(strength * deltaTime, 0f, 1f);
                var blended = velocity / speed + (force / forceLen - velocity / speed) * t;
                float len = blended.Length;
                velocity = len > 1e-6f ? blended * (speed / len) : velocity;
                break;
            }
            case ParticleForceMode.OverrideVelocity:
                velocity = force * strength;
                break;
            case ParticleForceMode.OverrideDirection:
            {
                float forceLen = force.Length;
                if (forceLen > 1e-6f)
                    velocity = force * (velocity.Length / forceLen);
                break;
            }
        }
    }

    // Zero out anything non-finite. A single NaN velocity poisons a particle forever.
    public static float3 FilterInvalid(in float3 v)
    {
        return new float3(
            float.IsFinite(v.x) ? v.x : 0f,
            float.IsFinite(v.y) ? v.y : 0f,
            float.IsFinite(v.z) ? v.z : 0f);
    }
}

// Uniform directional force in simulation space. Wind, updraft, sideways drift.
public sealed class LinearForce : PositionModuleBase
{
    public float3 Force;
    public ParticleForceMode Mode = ParticleForceMode.Additive;
    public float Strength = 1f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var velocities = PositionModule.Velocities.Slice(offset, count);
        if (Mode == ParticleForceMode.Additive)
        {
            var step = Force * (Strength * deltaTime);
            for (int i = 0; i < count; i++)
                velocities[i] += step;
            return;
        }
        for (int i = 0; i < count; i++)
            ParticleForceHelper.Apply(ref velocities[i], Mode, in Force, Strength, deltaTime);
    }
}

// Gravity as a single signed magnitude along an axis. Separate from LinearForce because the
// overwhelmingly common case is one number on one axis and a scalar field is easier to drive.
public sealed class GravityForce : PositionModuleBase
{
    public float3 Axis = float3.Up;
    public float Magnitude = -9.81f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var step = Axis * (Magnitude * deltaTime);
        if (step.LengthSquared < 1e-12f)
            return;
        var velocities = PositionModule.Velocities.Slice(offset, count);
        for (int i = 0; i < count; i++)
            velocities[i] += step;
    }
}

// Bleeds speed off every particle. Framed as a per-second fraction of the current speed, so Drag = 1
// means "lose all of it in a second" no matter the frame rate, and clamped at zero so a long frame
// can never flip a particle into reverse.
public sealed class VelocityDrag : PositionModuleBase
{
    public float Drag = 1f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        float keep = MathF.Max(0f, 1f - MathF.Max(0f, Drag) * deltaTime);
        var velocities = PositionModule.Velocities.Slice(offset, count);
        for (int i = 0; i < count; i++)
            velocities[i] *= keep;
    }
}

// Same idea as VelocityDrag, applied to spin instead of travel.
public sealed class AngularVelocityDrag : RotationModuleBase
{
    public float Drag = 1f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (RotationModule == null)
            return;
        float keep = MathF.Max(0f, 1f - MathF.Max(0f, Drag) * deltaTime);
        var angular = RotationModule.AngularVelocities.Slice(offset, count);
        for (int i = 0; i < count; i++)
            angular[i] *= keep;
    }
}

// Continuously feeds spin in. Paired with angular drag it settles at a terminal spin rate.
public sealed class ConstantAngularVelocityForce : RotationModuleBase
{
    // Radians per second per second, per axis.
    public float3 Force;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (RotationModule == null)
            return;
        var step = Force * deltaTime;
        var angular = RotationModule.AngularVelocities.Slice(offset, count);
        for (int i = 0; i < count; i++)
            angular[i] += step;
    }
}

// How a radial force's magnitude varies with distance from its centre.
public enum RadialForceMode
{
    // Gravity-like. Violent up close, which is what the distance clamps are for.
    InverseSquared,
    InverseLinear,
    Squared,
    Linear,

    // Same pull everywhere inside the clamp range.
    Constant,
}

// Shared body of the pull/push forces: work out the offset from a centre, turn distance into a
// magnitude by the chosen falloff, and push along it. Distance is clamped BEFORE the falloff so an
// inverse-square force cannot divide by a near-zero distance and fling a particle to infinity - the
// classic way a gravity well turns into a particle cannon. -xlinka
public abstract class RadialForceBase : PositionModuleBase
{
    public float Force = 1f;
    public RadialForceMode Mode = RadialForceMode.InverseSquared;
    public float MinDistance = 0.01f;
    public float MaxDistance = float.PositiveInfinity;
    public float MinForce = float.NegativeInfinity;
    public float MaxForce = float.PositiveInfinity;

    private float _minDistSqr;
    private float _maxDistSqr;
    private float _minDistInv;
    private float _maxDistInv;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void PrepareUpdate(float deltaTime)
    {
        float min = MathF.Max(MinDistance, 1e-4f);
        _minDistSqr = min * min;
        _maxDistSqr = MaxDistance * MaxDistance;
        _minDistInv = 1f / min;
        _maxDistInv = MaxDistance > 0f ? 1f / MaxDistance : 0f;
    }

    // Vector from the force's centre to the particle, in simulation space.
    protected abstract float3 GetOffset(int particleIndex);

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var velocities = PositionModule.Velocities.Slice(offset, count);
        float scaled = Force * deltaTime;

        for (int i = 0; i < count; i++)
        {
            var delta = GetOffset(offset + i);
            float sqrDistance = delta.LengthSquared;
            if (sqrDistance < 1e-12f)
                continue;
            float distance = MathF.Sqrt(sqrDistance);
            var direction = delta / distance;
            float invDistance = 1f / distance;

            float magnitude = Mode switch
            {
                RadialForceMode.InverseSquared => scaled / System.Math.Clamp(sqrDistance, _minDistSqr, _maxDistSqr),
                RadialForceMode.InverseLinear => scaled * System.Math.Clamp(invDistance, _maxDistInv, _minDistInv),
                RadialForceMode.Squared => scaled * System.Math.Clamp(sqrDistance, _minDistSqr, _maxDistSqr),
                RadialForceMode.Linear => scaled * System.Math.Clamp(distance, MinDistance, MaxDistance),
                RadialForceMode.Constant => scaled,
                _ => 0f,
            };
            magnitude = System.Math.Clamp(magnitude, MinForce, MaxForce);
            velocities[i] += ParticleForceHelper.FilterInvalid(direction * magnitude);
        }
    }
}

// Pushes particles away from a fixed point. Negative Force pulls them in.
public sealed class RadialForce : RadialForceBase
{
    public float3 Center;

    protected override float3 GetOffset(int particleIndex)
        => Simulation.RenderPositions[particleIndex] - Center;
}

// Pulls each particle back toward the exact point it was BORN at, not toward a shared centre. Good
// for effects that wander and snap back - dust disturbed and resettling, sparks tethered to a surface.
public sealed class OriginRadialForce : RadialForceBase
{
    protected override float3 GetOffset(int particleIndex)
        => Simulation.StartingPositions[particleIndex] - Simulation.RenderPositions[particleIndex];
}

// Pull (or push, with a negative Strength) toward a moving point, with a linear falloff to nothing at
// Range. Simpler and more predictable than RadialForce's falloff curves, which is what you want when
// the attractor is something a user is dragging around by hand.
public sealed class AttractorForce : PositionModuleBase
{
    public float3 Center;
    public float Strength = 4f;

    // Distance where the pull reaches zero. 0 means no falloff at all.
    public float Range;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var positions = Simulation.RenderPositions.Slice(offset, count);
        var velocities = PositionModule.Velocities.Slice(offset, count);
        float strength = Strength * deltaTime;
        float range = Range;

        for (int i = 0; i < count; i++)
        {
            var delta = Center - positions[i];
            float distance = delta.Length;
            if (distance < 1e-4f)
                continue;
            float falloff = range > 0f ? MathF.Max(0f, 1f - distance / range) : 1f;
            velocities[i] += delta / distance * (strength * falloff);
        }
    }
}

// Procedural churn from three phase-shifted sine octaves of the particle position, scrolled over time.
// It is a sine lattice, not gradient noise - it has visible structure if you sample it on a grid at
// low frequency - but at particle scale and particle density it reads as wind turbulence and costs a
// handful of sin() per particle instead of a full noise evaluation. -xlinka
public sealed class TurbulentForce : PositionModuleBase
{
    public ParticleForceMode Mode = ParticleForceMode.Additive;
    public float Strength = 1f;
    public float Frequency = 2f;
    public float ScrollSpeed = 0.6f;

    // Scroll phase in seconds. Advanced by ScrollSpeed each step; set directly to sync two systems.
    public float TimeOffset;

    private float _phase;

    public override ParticleSimPhase Phase => ParticleSimPhase.Force;

    public override void PrepareUpdate(float deltaTime)
    {
        TimeOffset += deltaTime * ScrollSpeed;
        _phase = TimeOffset;
    }

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        if (PositionModule == null)
            return;
        var positions = Simulation.RenderPositions.Slice(offset, count);
        var velocities = PositionModule.Velocities.Slice(offset, count);
        float frequency = MathF.Max(Frequency, 1e-3f);
        float time = _phase;
        float strength = Strength * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var p = positions[i] * frequency;
            var swirl = new float3(
                MathF.Sin(p.y + time * 1.7f) + MathF.Sin(p.z * 1.3f + time),
                MathF.Sin(p.z + time * 1.3f) + MathF.Sin(p.x * 1.7f + time),
                MathF.Sin(p.x + time) + MathF.Sin(p.y * 1.1f + time * 2.1f));
            ParticleForceHelper.Apply(ref velocities[i], Mode, in swirl, strength, deltaTime);
        }
    }
}
