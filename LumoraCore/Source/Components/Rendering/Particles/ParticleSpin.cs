// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Feeds spin in continuously. Paired with ParticleAngularDrag it settles at a terminal spin.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpin : ParticleModuleBase
{
    // Angular acceleration per axis, in radians per second squared.
    public readonly Sync<float3> Force = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = new float3(0f, 0f, 1f);
    }

    internal override ParticleSimModule CreateSimModule() => new ConstantAngularVelocityForce();

    internal override void PushParameters(ParticleSimModule module)
        => ((ConstantAngularVelocityForce)module).Force = Force.Value;
}
