// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits from a cylinder, solid or shell. Shell emission splits between the caps and the side wall by
// their relative area, so the distribution matches the real surface at any proportions.
[ComponentCategory("Rendering/Particles")]
public sealed class CylinderEmitter : ParticleEmitterBase
{
    public readonly Sync<float> Radius = new();
    public readonly Sync<float> Height = new();
    public readonly Sync<bool> FromShell = new();

    // Shell emission only: skip the flat ends and use the side wall alone.
    public readonly Sync<bool> ExcludeCaps = new();

    public readonly Sync<CylinderEmitterDirection> DirectionMode = new();
    public readonly Sync<CylinderEmitterCapsDirection> CapsDirectionMode = new();

    // Direction used by the Fixed modes, in slot-local space.
    public readonly Sync<float3> Direction = new();

    public override void OnInit()
    {
        base.OnInit();
        Radius.Value = 0.5f;
        Height.Value = 1f;
        DirectionMode.Value = CylinderEmitterDirection.CircleUniform;
        CapsDirectionMode.Value = CylinderEmitterCapsDirection.Facing;
        Direction.Value = float3.Up;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.CylinderEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var cylinder = (SimParticles.CylinderEmitter)emitter;
        cylinder.Transform = toSystemSpace;
        cylinder.Radius = Radius.Value;
        cylinder.Height = Height.Value;
        cylinder.EmitFromShell = FromShell.Value;
        cylinder.ExcludeCaps = ExcludeCaps.Value;
        cylinder.DirectionMode = DirectionMode.Value;
        cylinder.CapsDirectionMode = CapsDirectionMode.Value;
        cylinder.Direction = Direction.Value;
        cylinder.DirectionTransformMode = DirectionTransformMode.AsUnitDirection;
        cylinder.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
