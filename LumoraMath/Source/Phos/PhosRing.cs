// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A flat ring in the XZ plane around Y, extruded to Height. Unlike the torus this has hard edges and
// a square cross-section, which is what you want for dials, bezels, progress rings and pipe collars.
//
// Four bands are always built (top face, bottom face, outer wall, inner wall) even at Height 0, where
// the two walls collapse to zero area. Keeping the topology fixed means Height can animate without a
// rebuild, and a zero-area triangle costs nothing to rasterise. -xlinka
public class PhosRing : PhosShape
{
    public PhosVertex FirstVertex;

    public float InnerRadius = 0.4f;

    public float OuterRadius = 0.5f;

    // 0 gives a flat washer with degenerate walls
    public float Height = 0.05f;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int Segments;

    private const int RingCount = 8;

    private readonly int _cols;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Vertex rings, in build order. Each is _cols wide with the seam column duplicated so UVs wrap.
    private const int TopOuter = 0;
    private const int TopInner = 1;
    private const int BottomInner = 2;
    private const int BottomOuter = 3;
    private const int WallOuterBottom = 4;
    private const int WallOuterTop = 5;
    private const int WallInnerTop = 6;
    private const int WallInnerBottom = 7;

    // Constructors

    public PhosRing(PhosTriangleSubmesh submesh, int segments = 48) : base(submesh.Mesh)
    {
        Segments = System.Math.Max(3, segments);
        _cols = Segments + 1;
        _totalVertices = RingCount * _cols;
        _totalTriangles = 4 * Segments * 2;

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddRingGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateRingVertices();
    }

    private PhosVertex AddRingGeometry(PhosTriangleSubmesh submesh)
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

        // Band(a, b) faces along b-to-a crossed with the direction of increasing angle, matching the
        // cylinder's proven side winding. Ordering each pair picks which way the band faces outward.
        int triIndex = firstTriangle;
        Band(submesh, firstVertexIndex, ref triIndex, TopOuter, TopInner);
        Band(submesh, firstVertexIndex, ref triIndex, BottomInner, BottomOuter);
        Band(submesh, firstVertexIndex, ref triIndex, WallOuterBottom, WallOuterTop);
        Band(submesh, firstVertexIndex, ref triIndex, WallInnerTop, WallInnerBottom);

        return firstVertex;
    }

    private void Band(PhosTriangleSubmesh submesh, int firstVertexIndex, ref int triIndex, int lowerRing, int upperRing)
    {
        int lower = firstVertexIndex + lowerRing * _cols;
        int upper = firstVertexIndex + upperRing * _cols;
        for (int i = 0; i < Segments; i++)
        {
            submesh.SetTriangle(triIndex++, lower + i, lower + i + 1, upper + i + 1);
            submesh.SetTriangle(triIndex++, lower + i, upper + i + 1, upper + i);
        }
    }

    private void UpdateRingVertices()
    {
        float halfHeight = Height * 0.5f;
        float inner = System.Math.Min(InnerRadius, OuterRadius);
        float outer = System.Math.Max(InnerRadius, OuterRadius);

        WriteRing(TopOuter, outer, halfHeight, float3.Up, 0f);
        WriteRing(TopInner, inner, halfHeight, float3.Up, 1f);
        WriteRing(BottomInner, inner, -halfHeight, float3.Down, 1f);
        WriteRing(BottomOuter, outer, -halfHeight, float3.Down, 0f);
        WriteRing(WallOuterBottom, outer, -halfHeight, float3.Zero, 0f);
        WriteRing(WallOuterTop, outer, halfHeight, float3.Zero, 1f);
        WriteRing(WallInnerTop, inner, halfHeight, float3.Zero, 1f);
        WriteRing(WallInnerBottom, inner, -halfHeight, float3.Zero, 0f);
    }

    // A zero normal means "use the radial direction", outward for the outer walls and inward for the
    // inner ones, which is decided by the ring index.
    private void WriteRing(int ring, float radius, float y, float3 faceNormal, float v)
    {
        bool radial = faceNormal == float3.Zero;
        bool inward = ring == WallInnerTop || ring == WallInnerBottom;
        int index = FirstVertex.Index + ring * _cols;

        for (int i = 0; i < _cols; i++)
        {
            float angle = (float)i / Segments * MathF.PI * 2f;
            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);

            float3 normal = radial
                ? new float3(inward ? -cosA : cosA, 0f, inward ? -sinA : sinA)
                : faceNormal;

            Mesh.RawPositions[index] = new float3(cosA * radius, y, sinA * radius) + Position;
            Mesh.RawNormals[index] = normal;
            Mesh.RawTangents[index] = new float4(-sinA, 0f, cosA, 1f);
            Mesh.RawUV0s[index] = new float2((float)i / Segments * UVScale.x, v * UVScale.y);
            if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
            index++;
        }
    }
}
