// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Godot.Hooks;

// The engine's own decomposition is only reachable through a mesh instance: it leaves a static body of hull
// shapes under it, the points are read back off those, and the throwaway tree is freed. Nothing of it ever
// enters a scene, which is what makes building it on a worker thread allowed. -xlinka
public sealed class GodotConvexDecomposer : IConvexDecomposer
{
    public static void Install()
    {
        ConvexSplit.Backend ??= new GodotConvexDecomposer();
    }

    public List<float3[]> Decompose(float3[] positions, int[] indices, ConvexSplitSettings settings)
    {
        var hulls = new List<float3[]>();
        if (positions.Length == 0 || indices.Length < 3)
            return hulls;

        var vertices = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            vertices[i] = new Vector3(positions[i].x, positions[i].y, positions[i].z);

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var options = new MeshConvexDecompositionSettings
        {
            Mode = MeshConvexDecompositionSettings.ModeEnum.Voxel,
            Resolution = (uint)System.Math.Clamp(settings.Resolution, 10000, 100000),
            MaxConcavity = System.Math.Clamp(settings.MaxConcavity, 0.001f, 1f),
            MaxConvexHulls = (uint)System.Math.Clamp(settings.MaxHulls, 1, 32),
            PlaneDownsampling = (uint)System.Math.Clamp(settings.PlaneStep, 1, 16),
            ConvexHullDownsampling = (uint)System.Math.Clamp(settings.PlaneStep, 1, 16),
            MaxNumVerticesPerConvexHull = (uint)System.Math.Clamp(settings.MaxPointsPerHull, 4, 255),
            MinVolumePerConvexHull = 0.0001f,
            ProjectHullVertices = true,
            NormalizeMesh = false,
        };

        var holder = new MeshInstance3D { Mesh = mesh };
        try
        {
            holder.CreateMultipleConvexCollisions(options);
            foreach (var body in holder.GetChildren())
            {
                foreach (var child in body.GetChildren())
                {
                    if (child is not CollisionShape3D { Shape: ConvexPolygonShape3D convex })
                        continue;
                    var points = convex.Points;
                    var hull = new float3[points.Length];
                    for (int i = 0; i < points.Length; i++)
                        hull[i] = new float3(points[i].X, points[i].Y, points[i].Z);
                    hulls.Add(hull);
                }
            }
        }
        finally
        {
            holder.Free();
        }
        return hulls;
    }
}
