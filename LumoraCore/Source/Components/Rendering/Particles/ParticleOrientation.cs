// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Points each particle along its own velocity - stretched sparks, rain, debris that leads with its
// travel. Below MinimumVelocity the particle keeps whatever orientation it had, so one coming to rest
// does not snap to a random facing on residual noise.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleOrientByVelocity : ParticleModuleBase
{
    // Reference up axis, in the system's simulation space.
    public readonly Sync<float3> Up = new();

    // Below this speed the particle is left alone.
    public readonly Sync<float> MinimumVelocity = new();

    // Speed band above the minimum over which the alignment fades in. 0 snaps.
    public readonly Sync<float> VelocityTransitionRange = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        Up.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new OrientByVelocity();

    internal override void PushParameters(ParticleSimModule module)
    {
        var orient = (OrientByVelocity)module;
        orient.Up = Up.Value;
        orient.MinimumVelocity = MinimumVelocity.Value;
        orient.VelocityTransitionRange = VelocityTransitionRange.Value;
    }
}

// Points each particle at this component's slot. Sparks facing a flame core, shards facing in.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleOrientAtPoint : ParticleModuleBase
{
    // Reference up axis, in the system's simulation space.
    public readonly Sync<float3> Up = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        Up.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new OrientAtPoint();

    internal override void PushParameters(ParticleSimModule module)
    {
        var orient = (OrientAtPoint)module;
        var target = System.Target;
        orient.TargetPoint = target != null ? target.Slot.GlobalPointToLocal(Slot.GlobalPosition) : float3.Zero;
        orient.Up = Up.Value;
    }
}
