// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a disc, directed in a cone around the slot's up axis. The particles all start on the
// disc and only their DIRECTIONS flare, which is the nozzle shape - for particles spread through a
// solid cone use ConeVolumeEmitter instead.
[ComponentCategory("Rendering/Particles")]
public sealed class ConeEmitter : ParticleEmitterBase
{
    public readonly Sync<float> Radius = new();

    // Cone half-angle in degrees (0 = straight up, 90 = hemisphere).
    public readonly Sync<float> Angle = new();

    public override void OnInit()
    {
        base.OnInit();
        Radius.Value = 0.1f;
        Angle.Value = 20f;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.ConeSprayEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var cone = (SimParticles.ConeSprayEmitter)emitter;
        cone.Transform = toSystemSpace;
        cone.Radius = Radius.Value;
        cone.Angle = MathF.Min(MathF.Max(Angle.Value, 0f), 89.9f) * (MathF.PI / 180f);
        cone.Axis = float3.Up;
    }
}
