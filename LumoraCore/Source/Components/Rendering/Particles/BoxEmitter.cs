// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a box - inside its volume, or off its surface - along the slot's up axis.
[ComponentCategory("Rendering/Particles")]
public sealed class BoxEmitter : ParticleEmitterBase
{
    public readonly Sync<float3> Size = new();

    // Emit from the surface only instead of the full volume.
    public readonly Sync<bool> FromShell = new();

    // Aim particles out along the nearest face instead of along the slot's up axis.
    public readonly Sync<bool> FaceNormalDirection = new();

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = new float3(1f, 0.1f, 1f);
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.BoxEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var box = (SimParticles.BoxEmitter)emitter;
        box.Transform = toSystemSpace;
        box.Size = Size.Value;
        box.EmitFromShell = FromShell.Value;
        box.DirectionMode = FaceNormalDirection.Value
            ? BoxEmitterDirection.ClosestFaceNormal
            : BoxEmitterDirection.Fixed;
        box.Direction = float3.Up;
        box.DirectionTransformMode = DirectionTransformMode.AsUnitDirection;
        box.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
