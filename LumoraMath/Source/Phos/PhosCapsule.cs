// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A cylindrical body along Y capped by a hemisphere at each end. Height is the cylindrical section
// only, so the total extent along Y is Height + 2 * Radius, which is the same convention the capsule
// collider uses.
//
// One continuous latitude grid covers both hemispheres and the body. The two rows at the equator sit
// at -Height/2 and +Height/2 and share a horizontal normal, so the band between them is the cylinder
// and shades as one surface with the caps instead of showing a seam. Segment and ring counts are
// baked per instance (rebuild to change); radius and height update in place. -xlinka
public class PhosCapsule : PhosShape
{
    public PhosVertex FirstVertex;

    public float Radius = 0.5f;

    public float Height = 1f;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int Segments;

    public readonly int Rings;

    private readonly int _rows;
    private readonly int _cols;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Constructors

    public PhosCapsule(PhosTriangleSubmesh submesh, int segments = 24, int rings = 8) : base(submesh.Mesh)
    {
        Segments = System.Math.Max(3, segments);
        Rings = System.Math.Max(1, rings);

        // Two hemispheres of Rings bands each, plus the duplicated equator pair that bounds the body.
        _rows = 2 * Rings + 2;
        _cols = Segments + 1;
        _totalVertices = _rows * _cols;
        _totalTriangles = (_rows - 1) * Segments * 2;

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddCapsuleGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateCapsuleVertices();
    }

    private PhosVertex AddCapsuleGeometry(PhosTriangleSubmesh submesh)
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

        int triIndex = firstTriangle;
        for (int row = 0; row < _rows - 1; row++)
        {
            for (int col = 0; col < Segments; col++)
            {
                int bl = firstVertexIndex + row * _cols + col;
                int br = bl + 1;
                int tl = bl + _cols;
                int tr = tl + 1;

                // Same winding as the cylinder's side band: rows run bottom to top and columns follow
                // increasing angle, so this is the outward-facing order.
                submesh.SetTriangle(triIndex++, bl, br, tr);
                submesh.SetTriangle(triIndex++, bl, tr, tl);
            }
        }

        return firstVertex;
    }

    private void UpdateCapsuleVertices()
    {
        float halfHeight = Height * 0.5f;
        int index = FirstVertex.Index;

        for (int row = 0; row < _rows; row++)
        {
            // Rows 0..Rings walk the bottom cap from its pole to the equator; rows Rings+1..2*Rings+1
            // walk the top cap from the equator to its pole. Both equator rows sit at the same
            // latitude, which is what gives the body its own band.
            bool bottom = row <= Rings;
            int localRow = bottom ? row : row - (Rings + 1);
            float lat = (float)localRow / Rings * (MathF.PI * 0.5f);
            float sinLat = MathF.Sin(lat);
            float cosLat = MathF.Cos(lat);

            float ny = bottom ? -cosLat : sinLat;
            float nr = bottom ? sinLat : cosLat;
            float centerY = bottom ? -halfHeight : halfHeight;
            float v = (float)row / (_rows - 1) * UVScale.y;

            for (int col = 0; col < _cols; col++)
            {
                float angle = (float)col / Segments * MathF.PI * 2f;
                float cosA = MathF.Cos(angle);
                float sinA = MathF.Sin(angle);

                float3 normal = new float3(nr * cosA, ny, nr * sinA);
                float3 pos = normal * Radius + new float3(0f, centerY, 0f) + Position;

                Mesh.RawPositions[index] = pos;
                Mesh.RawNormals[index] = normal;
                Mesh.RawTangents[index] = new float4(-sinA, 0f, cosA, 1f);
                Mesh.RawUV0s[index] = new float2((float)col / Segments * UVScale.x, v);
                if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
                index++;
            }
        }
    }
}
