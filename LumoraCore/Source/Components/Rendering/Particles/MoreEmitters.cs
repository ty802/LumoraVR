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

// Emits along a segment, with independent colours and directions at each end interpolated along it.
// Beams, trails behind a swung object, edge highlights.
[ComponentCategory("Rendering/Particles")]
public sealed class LineEmitter : ParticleEmitterBase
{
    public readonly Sync<float3> Point0 = new();
    public readonly Sync<float3> Point1 = new();
    public readonly Sync<colorHDR> Color0 = new();
    public readonly Sync<colorHDR> Color1 = new();
    public readonly Sync<LineEmitterDirection> DirectionMode = new();
    public readonly Sync<float3> Direction0 = new();
    public readonly Sync<float3> Direction1 = new();

    public override void OnInit()
    {
        base.OnInit();
        Point0.Value = float3.Left;
        Point1.Value = float3.Right;
        Color0.Value = colorHDR.White;
        Color1.Value = colorHDR.White;
        Direction0.Value = float3.Up;
        Direction1.Value = float3.Up;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.LineEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var line = (SimParticles.LineEmitter)emitter;
        line.Transform = toSystemSpace;
        line.Point0 = Point0.Value;
        line.Point1 = Point1.Value;
        line.Color0 = Color0.Value;
        line.Color1 = Color1.Value;
        line.DirectionMode = DirectionMode.Value;
        line.Direction0 = Direction0.Value;
        line.Direction1 = Direction1.Value;
        line.UpDirection = float3.Up;
        line.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}

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

// Emits from the geometry of a mesh - its vertices, along its edges, or across its faces. Points at a
// ProceduralMesh or MeshProvider and follows it as it deforms.
[ComponentCategory("Rendering/Particles")]
public sealed class MeshEmitter : ParticleEmitterBase
{
    // Geometry source: a ProceduralMesh or MeshProvider.
    public readonly SyncRef<Component> SourceMesh = new();

    public readonly Sync<MeshEmissionSource> EmitFrom = new();

    // Multiply the emitted colour by the mesh's vertex colour where it has one.
    public readonly Sync<bool> UseVertexColors = new();

    public readonly Sync<MeshEmitterDirection> DirectionMode = new();

    // Authored direction, read in the frame DirectionMode selects.
    public readonly Sync<float3> Direction = new();

    public override void OnInit()
    {
        base.OnInit();
        EmitFrom.Value = MeshEmissionSource.Faces;
        UseVertexColors.Value = true;
        DirectionMode.Value = MeshEmitterDirection.TangentSpace;
        Direction.Value = float3.Forward;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new MeshSurfaceEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var mesh = (MeshSurfaceEmitter)emitter;
        mesh.Transform = toSystemSpace;
        mesh.Mesh = SourceMesh.Target switch
        {
            Meshes.ProceduralMesh procedural => procedural.PhosMesh,
            MeshProvider provider => provider.Asset?.MeshData,
            _ => null
        };
        mesh.EmitFrom = EmitFrom.Value;
        mesh.UseVertexColors = UseVertexColors.Value;
        mesh.DirectionMode = DirectionMode.Value;
        mesh.Direction = Direction.Value;
        mesh.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
