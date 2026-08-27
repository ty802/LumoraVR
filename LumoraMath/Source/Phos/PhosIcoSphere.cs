// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// An icosahedron with each face split into four, repeated, and every vertex pushed out onto the
// sphere. Triangles come out near-equilateral and evenly sized, which is what makes it better than a
// latitude/longitude sphere for physics proxies, soft bodies, impostors and anything that shades
// badly when the poles pinch.
//
// Triangles are unwelded: every face owns its three vertices. That is what lets flat shading and the
// UV seam both work without adjacency bookkeeping. A spherical UV wrap is discontinuous at the back
// meridian and undefined at the poles, and with shared vertices there is no way to give one vertex
// two different U values. The cost is roughly six times the vertex count of a welded sphere, which
// is why the subdivision level is capped. -xlinka
public class PhosIcoSphere : PhosShape
{
    // 5 is already 20480 triangles
    public const int MaxSubdivisions = 5;

    public PhosVertex FirstVertex;

    public float Radius = 0.5f;

    public bool FlatShading;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int Subdivisions;

    private readonly int _totalVertices;
    private readonly int _totalTriangles;
    private readonly float3[] _directions;
    private readonly float3[] _faceNormals;
    private readonly float2[] _uvs;
    private readonly float4[] _tangents;

    // Constructors

    public PhosIcoSphere(PhosTriangleSubmesh submesh, int subdivisions = 2) : base(submesh.Mesh)
    {
        Subdivisions = System.Math.Clamp(subdivisions, 0, MaxSubdivisions);

        _directions = BuildDirections(Subdivisions);
        _totalVertices = _directions.Length;
        _totalTriangles = _totalVertices / 3;
        _faceNormals = new float3[_totalTriangles];
        _uvs = new float2[_totalVertices];
        _tangents = new float4[_totalVertices];
        BuildFaceData();

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddSphereGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateSphereVertices();
    }

    private PhosVertex AddSphereGeometry(PhosTriangleSubmesh submesh)
    {
        PhosMesh mesh = submesh.Mesh;

        mesh.IncreaseVertexCount(_totalVertices);
        PhosVertex firstVertex = mesh.GetVertex(mesh.VertexCount - _totalVertices);
        int firstVertexIndex = firstVertex.IndexUnsafe;

        int firstTriangle = 0;
        for (int i = 0; i < _totalTriangles; i++)
        {
            PhosTriangle triangle = submesh.AddTriangle();
            if (i == 0)
                firstTriangle = triangle.IndexUnsafe;
            AllTriangles?.Add(triangle);
        }

        // The direction list is already stored front-facing, so indices are sequential.
        for (int i = 0; i < _totalTriangles; i++)
        {
            int v = firstVertexIndex + i * 3;
            submesh.SetTriangle(firstTriangle + i, v, v + 1, v + 2);
        }

        return firstVertex;
    }

    private void UpdateSphereVertices()
    {
        int index = FirstVertex.Index;
        for (int i = 0; i < _totalVertices; i++)
        {
            float3 dir = _directions[i];
            Mesh.RawPositions[index] = dir * Radius + Position;
            Mesh.RawNormals[index] = FlatShading ? _faceNormals[i / 3] : dir;
            Mesh.RawTangents[index] = _tangents[i];
            Mesh.RawUV0s[index] = new float2(_uvs[i].x * UVScale.x, _uvs[i].y * UVScale.y);
            if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
            index++;
        }
    }

    private void BuildFaceData()
    {
        for (int t = 0; t < _totalTriangles; t++)
        {
            int v0 = t * 3;
            float3 a = _directions[v0];
            float3 b = _directions[v0 + 1];
            float3 c = _directions[v0 + 2];

            // Vertices are stored in the engine's front-facing order, whose right-hand normal points
            // inward, so the outward facet normal is the negated cross product.
            float3 n = float3.Cross(b - a, c - a);
            float len = n.Length;
            _faceNormals[t] = len > 1e-12f ? -(n / len) : (a + b + c).Normalized;

            float u0 = SphericalU(a);
            float u1 = SphericalU(b);
            float u2 = SphericalU(c);

            // A triangle straddling the back meridian has one or two corners that wrapped to nearly 0
            // while the rest sit near 1. Push the low ones past 1 so the interpolation crosses the
            // seam instead of running the whole way back around the sphere.
            float maxU = System.Math.Max(u0, System.Math.Max(u1, u2));
            float minU = System.Math.Min(u0, System.Math.Min(u1, u2));
            if (maxU - minU > 0.5f)
            {
                if (u0 < 0.5f) u0 += 1f;
                if (u1 < 0.5f) u1 += 1f;
                if (u2 < 0.5f) u2 += 1f;
            }

            // A pole vertex has no meridian of its own; borrow the midpoint of the other two so the
            // triangle's texture wedge stays straight instead of collapsing to U = 0.
            if (IsPole(a)) u0 = (u1 + u2) * 0.5f;
            if (IsPole(b)) u1 = (u0 + u2) * 0.5f;
            if (IsPole(c)) u2 = (u0 + u1) * 0.5f;

            _uvs[v0] = new float2(u0, SphericalV(a));
            _uvs[v0 + 1] = new float2(u1, SphericalV(b));
            _uvs[v0 + 2] = new float2(u2, SphericalV(c));

            _tangents[v0] = Tangent(a);
            _tangents[v0 + 1] = Tangent(b);
            _tangents[v0 + 2] = Tangent(c);
        }
    }

    private static bool IsPole(float3 dir) => System.Math.Abs(dir.y) > 0.99999f;

    private static float SphericalU(float3 dir)
    {
        float u = MathF.Atan2(dir.z, dir.x) / (MathF.PI * 2f) + 0.5f;
        return u;
    }

    private static float SphericalV(float3 dir)
    {
        return MathF.Asin(System.Math.Clamp(dir.y, -1f, 1f)) / MathF.PI + 0.5f;
    }

    private static float4 Tangent(float3 dir)
    {
        // Along increasing U, which on a sphere is the horizontal circle through the point.
        float3 t = new float3(-dir.z, 0f, dir.x);
        float len = t.Length;
        if (len < 1e-6f)
            return new float4(1f, 0f, 0f, 1f);
        t /= len;
        return new float4(t.x, t.y, t.z, 1f);
    }

    private static float3[] BuildDirections(int subdivisions)
    {
        float t = (1f + MathF.Sqrt(5f)) * 0.5f;
        float3[] baseVerts =
        {
            new float3(-1f, t, 0f).Normalized,
            new float3(1f, t, 0f).Normalized,
            new float3(-1f, -t, 0f).Normalized,
            new float3(1f, -t, 0f).Normalized,
            new float3(0f, -1f, t).Normalized,
            new float3(0f, 1f, t).Normalized,
            new float3(0f, -1f, -t).Normalized,
            new float3(0f, 1f, -t).Normalized,
            new float3(t, 0f, -1f).Normalized,
            new float3(t, 0f, 1f).Normalized,
            new float3(-t, 0f, -1f).Normalized,
            new float3(-t, 0f, 1f).Normalized,
        };

        // Standard icosahedron faces, wound so the right-hand normal points outward.
        int[] baseFaces =
        {
            0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
            1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
            3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
            4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
        };

        var current = new List<float3>(baseFaces.Length);
        for (int i = 0; i < baseFaces.Length; i += 3)
        {
            current.Add(baseVerts[baseFaces[i]]);
            current.Add(baseVerts[baseFaces[i + 1]]);
            current.Add(baseVerts[baseFaces[i + 2]]);
        }

        for (int level = 0; level < subdivisions; level++)
        {
            var next = new List<float3>(current.Count * 4);
            for (int i = 0; i < current.Count; i += 3)
            {
                float3 a = current[i];
                float3 b = current[i + 1];
                float3 c = current[i + 2];
                float3 ab = ((a + b) * 0.5f).Normalized;
                float3 bc = ((b + c) * 0.5f).Normalized;
                float3 ca = ((c + a) * 0.5f).Normalized;

                next.Add(a); next.Add(ab); next.Add(ca);
                next.Add(ab); next.Add(b); next.Add(bc);
                next.Add(ca); next.Add(bc); next.Add(c);
                next.Add(ab); next.Add(bc); next.Add(ca);
            }
            current = next;
        }

        // Flip to the engine's front-facing order once, at the end.
        var result = new float3[current.Count];
        for (int i = 0; i < current.Count; i += 3)
        {
            result[i] = current[i];
            result[i + 1] = current[i + 2];
            result[i + 2] = current[i + 1];
        }
        return result;
    }
}
