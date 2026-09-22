// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a flat disc or ring. Shockwaves, portal rims, ground-level swirls.
[ComponentCategory("Rendering/Particles")]
public sealed class CircleEmitter : ParticleEmitterBase
{
    public readonly Sync<float> Radius = new();

    // Per-axis scale of the disc, so it can be an ellipse.
    public readonly Sync<float2> Scale = new();

    // Emit from the rim only instead of the whole disc.
    public readonly Sync<bool> FromShell = new();

    // Which plane the disc lies in, in slot-local space.
    public readonly Sync<CircleEmitterAlignment> Alignment = new();

    public readonly Sync<CircleEmitterDirection> DirectionMode = new();

    // Direction used by the Fixed modes, in slot-local space.
    public readonly Sync<float3> Direction = new();

    public override void OnInit()
    {
        base.OnInit();
        Radius.Value = 1f;
        Scale.Value = float2.One;
        Alignment.Value = CircleEmitterAlignment.XZ;
        DirectionMode.Value = CircleEmitterDirection.RadialUniform;
        Direction.Value = float3.Up;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.CircleEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var circle = (SimParticles.CircleEmitter)emitter;
        circle.Transform = toSystemSpace;
        circle.Radius = Radius.Value;
        circle.Scale = Scale.Value;
        circle.EmitFromShell = FromShell.Value;
        circle.Alignment = Alignment.Value;
        circle.DirectionMode = DirectionMode.Value;
        circle.Direction = Direction.Value;
        circle.DirectionTransformMode = DirectionTransformMode.AsUnitDirection;
        circle.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
