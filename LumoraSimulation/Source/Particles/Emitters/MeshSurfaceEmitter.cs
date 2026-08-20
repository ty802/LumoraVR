// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Simulation.Particles.Emitters;

// Emits from the geometry of a mesh - its vertices, along its edges, or across its faces. Face
// emission uses a uniform barycentric sample per triangle, but picks the triangle uniformly by INDEX
// rather than by area, so a mesh with wildly uneven triangle sizes concentrates particles on its
// dense regions. That is a deliberate trade: area weighting needs a cumulative table rebuilt whenever
// the mesh deforms, and these emitters are usually pointed at meshes that deform every frame. -xlinka
public sealed class MeshSurfaceEmitter : TransformableSimEmitter
{
    private readonly List<int> _triangles = new();
    private PhosMesh? _cachedMesh;
    private int _cachedTriangleCount = -1;

    // Source geometry. Read only, never modified.
    public PhosMesh? Mesh;

    public MeshEmissionSource EmitFrom = MeshEmissionSource.Faces;

    // Multiply the emitted colour by the mesh's vertex colour where it has one.
    public bool UseVertexColors = true;

    public MeshEmitterDirection DirectionMode = MeshEmitterDirection.TangentSpace;

    // Authored direction, read in the frame DirectionMode selects.
    public float3 Direction = float3.Forward;

    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => UseVertexColors && (Mesh?.HasColors ?? false);
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var mesh = Mesh;
        if (mesh == null || mesh.VertexCount == 0)
            return 0;

        var meshPositions = mesh.RawPositions;
        var meshColors = mesh.HasColors ? mesh.RawColors : null;
        bool writeColors = InitializesColors && meshColors != null;

        // Tangent space needs both a normal and a tangent; without them fall back rather than emit junk.
        var mode = DirectionMode;
        if (mode == MeshEmitterDirection.TangentSpace && (!mesh.HasNormals || !mesh.HasTangents))
            mode = MeshEmitterDirection.LocalSpace;
        var meshNormals = mode == MeshEmitterDirection.TangentSpace ? mesh.RawNormals : null;
        var meshTangents = mode == MeshEmitterDirection.TangentSpace ? mesh.RawTangents : null;

        var localDirection = DirectionTransformHelper.Transform(
            in Transform, in Direction, DirectionTransformMode.AsVector);

        var random = Simulation.Random;
        int written = 0;

        if (EmitFrom == MeshEmissionSource.Vertices)
        {
            for (int i = 0; i < count; i++)
            {
                int vertex = random.Range(0, mesh.VertexCount);
                positions[written] = TransformPoint(meshPositions[vertex]);
                directions[written] = mode == MeshEmitterDirection.TangentSpace
                    ? TangentDirection(meshNormals![vertex], meshTangents![vertex])
                    : localDirection;
                if (writeColors)
                    colors[written] = meshColors![vertex];
                written++;
            }
        }
        else
        {
            EnsureTriangleCache(mesh);
            int triangleCount = _triangles.Count / 3;
            if (triangleCount == 0)
                return 0;

            for (int i = 0; i < count; i++)
            {
                int triangle = random.Range(0, triangleCount) * 3;
                int a = _triangles[triangle];
                int b = _triangles[triangle + 1];
                int c = _triangles[triangle + 2];

                // Edge emission collapses the barycentric coordinate onto one edge of the triangle.
                var bary = EmitFrom == MeshEmissionSource.Edges
                    ? EdgeBarycentric(random)
                    : random.BarycentricCoordinate;

                var point = meshPositions[a] * bary.x + meshPositions[b] * bary.y + meshPositions[c] * bary.z;
                positions[written] = TransformPoint(point);

                if (mode == MeshEmitterDirection.TangentSpace)
                {
                    var normal = meshNormals![a] * bary.x + meshNormals![b] * bary.y + meshNormals![c] * bary.z;
                    var tangent = meshTangents![a] * bary.x + meshTangents![b] * bary.y + meshTangents![c] * bary.z;
                    directions[written] = TangentDirection(normal, tangent);
                }
                else
                {
                    directions[written] = localDirection;
                }

                if (writeColors)
                {
                    colors[written] = meshColors![a] * bary.x + meshColors![b] * bary.y + meshColors![c] * bary.z;
                }
                written++;
            }
        }

        DirectionTransformHelper.ApplyRandomSpread(directions.Slice(0, written), random, RandomDirectionWeight);
        return written;
    }

    private static float3 EdgeBarycentric(SeededRandom random)
    {
        float t = random.Value;
        return random.Range(0, 3) switch
        {
            0 => new float3(1f - t, t, 0f),
            1 => new float3(0f, 1f - t, t),
            _ => new float3(t, 0f, 1f - t),
        };
    }

    private float3 TangentDirection(in float3 normal, in float4 tangent)
    {
        var n = normal.Normalized;
        var t = new float3(tangent.x, tangent.y, tangent.z);
        float tlen = t.Length;
        if (tlen < 1e-6f)
            return TransformVector(n * Direction.z);
        t /= tlen;
        var bitangent = float3.Cross(n, t) * (tangent.w >= 0f ? 1f : -1f);
        var local = t * Direction.x + bitangent * Direction.y + n * Direction.z;
        return TransformVector(local);
    }

    // Flatten every triangle submesh into one index list. Rebuilt when the mesh reference or its
    // triangle count changes; a mesh that only moves its existing vertices keeps the cache.
    private void EnsureTriangleCache(PhosMesh mesh)
    {
        int total = 0;
        for (int s = 0; s < mesh.Submeshes.Count; s++)
        {
            if (mesh.Submeshes[s].Topology == PhosTopology.Triangles)
                total += mesh.Submeshes[s].Count;
        }
        if (ReferenceEquals(_cachedMesh, mesh) && _cachedTriangleCount == total)
            return;

        _cachedMesh = mesh;
        _cachedTriangleCount = total;
        _triangles.Clear();
        for (int s = 0; s < mesh.Submeshes.Count; s++)
        {
            var submesh = mesh.Submeshes[s];
            if (submesh.Topology != PhosTopology.Triangles)
                continue;
            var indices = submesh.RawIndices;
            int indexCount = submesh.IndexCount;
            for (int i = 0; i + 2 < indexCount; i += 3)
            {
                _triangles.Add(indices[i]);
                _triangles.Add(indices[i + 1]);
                _triangles.Add(indices[i + 2]);
            }
        }
    }
}
