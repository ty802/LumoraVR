// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// Not a PhosShape: every other generator here bakes its topology once and then only moves vertices,
// but a hull's vertex and triangle counts fall out of the input, so there is nothing stable to bake.
// This rebuilds the mesh outright each time instead of pretending otherwise. -xlinka
public static class PhosConvexHull
{
    // Returns what the solver made of the input, so a caller can tell an empty result from a flat one.
    public static ConvexHullSolver.Result Build(
        PhosMesh mesh,
        IReadOnlyList<float3> points,
        float minPointDistance,
        bool flatShading,
        float2 uvScale,
        color? vertexColor = null)
    {
        mesh.Clear();

        var hullPoints = new List<float3>();
        var hullIndices = new List<int>();
        ConvexHullSolver.Result result = ConvexHullSolver.Solve(points, minPointDistance, hullPoints, hullIndices);

        if (hullIndices.Count == 0)
            return result;

        mesh.HasNormals = true;
        mesh.HasTangents = true;
        mesh.HasUV0s = true;
        mesh.HasColors = vertexColor.HasValue;

        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);

        float3 centroid = float3.Zero;
        for (int i = 0; i < hullPoints.Count; i++)
            centroid += hullPoints[i];
        centroid /= hullPoints.Count;

        if (flatShading)
            BuildFlat(mesh, submesh, hullPoints, hullIndices, centroid, uvScale, vertexColor);
        else
            BuildSmooth(mesh, submesh, hullPoints, hullIndices, centroid, uvScale, vertexColor);

        return result;
    }

    private static void BuildSmooth(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        List<float3> hullPoints,
        List<int> hullIndices,
        float3 centroid,
        float2 uvScale,
        color? vertexColor)
    {
        mesh.IncreaseVertexCount(hullPoints.Count);
        int first = mesh.VertexCount - hullPoints.Count;

        for (int i = 0; i < hullPoints.Count; i++)
        {
            float3 p = hullPoints[i];
            // Away from the centroid is the only normal a shared hull vertex can have. It is not the
            // averaged face normal, but on a convex body the two only diverge where the hull is very
            // lopsided, and this needs no adjacency to compute.
            float3 dir = (p - centroid).Normalized;
            if (dir.LengthSquared < 0.5f)
                dir = float3.Up;

            WriteVertex(mesh, first + i, p, dir, dir, uvScale, vertexColor);
        }

        int triangleCount = hullIndices.Count / 3;
        for (int t = 0; t < triangleCount; t++)
        {
            PhosTriangle triangle = submesh.AddTriangle();
            // The solver winds triangles counter-clockwise seen from outside; the engine's front face
            // is the other way round, so two corners swap.
            submesh.SetTriangle(
                triangle.IndexUnsafe,
                first + hullIndices[t * 3],
                first + hullIndices[t * 3 + 2],
                first + hullIndices[t * 3 + 1]);
        }
    }

    private static void BuildFlat(
        PhosMesh mesh,
        PhosTriangleSubmesh submesh,
        List<float3> hullPoints,
        List<int> hullIndices,
        float3 centroid,
        float2 uvScale,
        color? vertexColor)
    {
        int triangleCount = hullIndices.Count / 3;
        mesh.IncreaseVertexCount(triangleCount * 3);
        int first = mesh.VertexCount - triangleCount * 3;

        for (int t = 0; t < triangleCount; t++)
        {
            float3 a = hullPoints[hullIndices[t * 3]];
            float3 b = hullPoints[hullIndices[t * 3 + 1]];
            float3 c = hullPoints[hullIndices[t * 3 + 2]];

            float3 n = float3.Cross(b - a, c - a);
            float len = n.Length;
            float3 normal = len > 1e-12f ? n / len : float3.Up;

            int v = first + t * 3;
            WriteVertex(mesh, v, a, normal, (a - centroid).Normalized, uvScale, vertexColor);
            WriteVertex(mesh, v + 1, b, normal, (b - centroid).Normalized, uvScale, vertexColor);
            WriteVertex(mesh, v + 2, c, normal, (c - centroid).Normalized, uvScale, vertexColor);

            PhosTriangle triangle = submesh.AddTriangle();
            submesh.SetTriangle(triangle.IndexUnsafe, v, v + 2, v + 1);
        }
    }

    private static void WriteVertex(PhosMesh mesh, int index, float3 position, float3 normal, float3 uvDirection, float2 uvScale, color? vertexColor)
    {
        mesh.RawPositions[index] = position;
        mesh.RawNormals[index] = normal;

        float3 t = float3.Cross(float3.Up, normal);
        if (t.LengthSquared < 1e-8f)
            t = float3.Right;
        t = t.Normalized;
        mesh.RawTangents[index] = new float4(t.x, t.y, t.z, 1f);

        // Spherical wrap off the direction from the centre. A hull has no natural unwrap, and this at
        // least gives a continuous, predictable one for tri-planar-free texturing.
        float u = MathF.Atan2(uvDirection.z, uvDirection.x) / (MathF.PI * 2f) + 0.5f;
        float v = MathF.Asin(System.Math.Clamp(uvDirection.y, -1f, 1f)) / MathF.PI + 0.5f;
        mesh.RawUV0s[index] = new float2(u * uvScale.x, v * uvScale.y);

        if (vertexColor.HasValue)
            mesh.RawColors[index] = vertexColor.Value;
    }
}
