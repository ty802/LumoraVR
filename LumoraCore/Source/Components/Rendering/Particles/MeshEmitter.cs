// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

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
