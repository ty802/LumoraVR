// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Offsets a particle from its own position by an amount that grows with its speed, in the particle's
// own rotated frame.
//
//     renderPosition = position + rotation * clamp(speed * baseOffset, min, max)
//
// It is what stops a stretched particle from growing out of its own middle. A smoke puff or a spark
// that lengthens with speed should stay anchored at its trailing end and extend forwards; without
// this it grows equally in both directions and the effect appears to slide backwards out of whatever
// emitted it. Rotating the offset is what keeps that anchor pointing along the particle's facing
// rather than along the world.
//
// ONE MODULE, NO STORED COLUMN. The offset depends on nothing but this frame's velocity and
// rotation, so a per-particle pivot buffer filled by one pass and applied by another would only ever
// hold what this single pass computes, at the cost of a column per particle. -xlinka
public sealed class PivotBySpeed : PositionModuleBase
{
    // Direction and scale of the offset, per metre per second of speed. Forward by default, which is
    // the axis a stretched billboard grows along.
    public float3 BaseOffset = float3.Forward;

    public float3 ClampMin = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    public float3 ClampMax = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    // After the integrator has moved the particle and after any orientation override, because the
    // offset is expressed in the rotation this frame ended up with.
    public override ParticleSimPhase Phase => ParticleSimPhase.Output;
    public override ParticleRenderChannel Consumes => ParticleRenderChannel.Position | ParticleRenderChannel.Rotation;
    public override ParticleRenderChannel Produces => ParticleRenderChannel.Position;

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var target = Simulation.RenderPositions.Slice(offset, count);

        // No integrator means no velocities, so there is no speed to scale by and the particle stays
        // exactly where it is.
        if (PositionModule == null)
            return;

        var rotations = SourceRotations.Slice(offset, count);
        var velocities = PositionModule.Velocities.Slice(offset, count);

        for (int i = 0; i < count; i++)
        {
            float speed = velocities[i].Length;
            var pivot = new float3(
                System.Math.Clamp(speed * BaseOffset.x, ClampMin.x, ClampMax.x),
                System.Math.Clamp(speed * BaseOffset.y, ClampMin.y, ClampMax.y),
                System.Math.Clamp(speed * BaseOffset.z, ClampMin.z, ClampMax.z));

            target[i] += rotations[i] * pivot;
        }
    }
}
