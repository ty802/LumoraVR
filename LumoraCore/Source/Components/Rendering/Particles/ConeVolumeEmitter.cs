// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a solid cone (or its lateral surface), particles spread through the whole shape. For a
// nozzle spray - every particle starting on a small disc with only the directions flaring - use
// ConeEmitter instead.
[ComponentCategory("Rendering/Particles")]
public sealed class ConeVolumeEmitter : ParticleEmitterBase
{
    public readonly Sync<float> BaseRadius = new();
    public readonly Sync<float> Height = new();
    public readonly Sync<bool> FromShell = new();
    public readonly Sync<ConeEmitterDirection> DirectionMode = new();

    // Direction used by the Fixed mode, in slot-local space.
    public readonly Sync<float3> Direction = new();

    public override void OnInit()
    {
        base.OnInit();
        BaseRadius.Value = 0.5f;
        Height.Value = 1f;
        DirectionMode.Value = ConeEmitterDirection.RadialUniform;
        Direction.Value = float3.Up;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.ConeEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var cone = (SimParticles.ConeEmitter)emitter;
        cone.Transform = toSystemSpace;
        cone.BaseRadius = BaseRadius.Value;
        cone.Height = Height.Value;
        cone.EmitFromShell = FromShell.Value;
        cone.DirectionMode = DirectionMode.Value;
        cone.Direction = Direction.Value;
        cone.DirectionTransformMode = DirectionTransformMode.AsUnitDirection;
        cone.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
