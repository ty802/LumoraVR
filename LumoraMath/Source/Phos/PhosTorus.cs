// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// The ring lies flat in the XZ plane around the Y axis, so it reads as a rotation ring by default.
// Segment counts are baked into the topology per instance (rebuild to change); radii and UVs update
// in place.
public class PhosTorus : PhosShape
{
    public PhosVertex FirstVertex;

    // from the center to the middle of the tube
    public float MajorRadius = 0.25f;

    public float MinorRadius = 0.005f;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int MajorSegments;

    public readonly int MinorSegments;

    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Constructors

    public PhosTorus(PhosTriangleSubmesh submesh, int majorSegments = 48, int minorSegments = 8) : base(submesh.Mesh)
    {
        // A closed ring needs at least 3 segments per circle.
        MajorSegments = System.Math.Max(3, majorSegments);
        MinorSegments = System.Math.Max(3, minorSegments);

        // Duplicate the seam column/row so UVs wrap without stretching.
        _totalVertices = (MajorSegments + 1) * (MinorSegments + 1);
        _totalTriangles = MajorSegments * MinorSegments * 2;

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddTorusGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateTorusVertices();
    }

    private PhosVertex AddTorusGeometry(PhosTriangleSubmesh submesh)
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

        // Connect triangles
        int triIndex = firstTriangle;
        int stride = MinorSegments + 1;
        for (int i = 0; i < MajorSegments; i++)
        {
            for (int j = 0; j < MinorSegments; j++)
            {
                int v00 = firstVertexIndex + i * stride + j;
                int v10 = firstVertexIndex + (i + 1) * stride + j;
                int v01 = firstVertexIndex + i * stride + j + 1;
                int v11 = firstVertexIndex + (i + 1) * stride + j + 1;

                // Winding chosen so the outer surface is the front face (clockwise-front convention,
                // matched against the cylinder's proven side winding).
                submesh.SetTriangle(triIndex++, v00, v10, v01);
                submesh.SetTriangle(triIndex++, v10, v11, v01);
            }
        }

        return firstVertex;
    }

    private void UpdateTorusVertices()
    {
        int m = MajorSegments;
        int n = MinorSegments;
        float ringRadius = MajorRadius;
        float tubeRadius = MinorRadius;

        int index = FirstVertex.Index;
        for (int i = 0; i <= m; i++)
        {
            float u = (float)i / m * MathF.PI * 2f;
            float cosU = MathF.Cos(u);
            float sinU = MathF.Sin(u);

            for (int j = 0; j <= n; j++)
            {
                float v = (float)j / n * MathF.PI * 2f;
                float cosV = MathF.Cos(v);
                float sinV = MathF.Sin(v);

                float radial = ringRadius + tubeRadius * cosV;

                // Ring around Y; the tube is swept in the plane containing Y and the outward radial
                // direction, giving the analytic outward normal directly.
                float3 pos = new float3(radial * cosU, tubeRadius * sinV, radial * sinU) + Position;
                float3 normal = new float3(cosV * cosU, sinV, cosV * sinU);
                float4 tangent = new float4(-sinU, 0f, cosU, 1f);
                float2 uv = new float2((float)i / m * UVScale.x, (float)j / n * UVScale.y);

                Mesh.RawPositions[index] = pos;
                Mesh.RawNormals[index] = normal;
                Mesh.RawTangents[index] = tangent;
                Mesh.RawUV0s[index] = uv;
                if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
                index++;
            }
        }
    }
}
