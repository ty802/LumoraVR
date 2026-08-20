// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from the slot origin, along the slot's up axis.
[ComponentCategory("Rendering/Particles")]
public sealed class PointEmitter : ParticleEmitterBase
{
    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.PointEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var point = (SimParticles.PointEmitter)emitter;
        point.Position = toSystemSpace.MultiplyPoint(float3.Zero);
        point.Direction = toSystemSpace.MultiplyVector(float3.Up);
        point.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
