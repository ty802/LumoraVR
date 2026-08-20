// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a sphere (volume or shell), directed radially outward.
[ComponentCategory("Rendering/Particles")]
public sealed class SphereEmitter : ParticleEmitterBase
{
    public readonly Sync<float> Radius = new();

    // Emit from the surface only instead of the full volume.
    public readonly Sync<bool> FromShell = new();

    // Shifts what "outward" means, so the sphere can spray off to one side.
    public readonly Sync<float3> DirectionReferencePoint = new();

    public override void OnInit()
    {
        base.OnInit();
        Radius.Value = 0.5f;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.SphereEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var sphere = (SimParticles.SphereEmitter)emitter;
        sphere.Transform = toSystemSpace;
        sphere.Radius = Radius.Value;
        sphere.EmitFromShell = FromShell.Value;
        sphere.DirectionMode = SphereEmitterDirection.RadialUniform;
        sphere.DirectionReferencePoint = DirectionReferencePoint.Value;
        sphere.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
