// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

/// <summary>
/// Subdivided plane on the local XY plane (normal +Z), (SegmentsX+1)x(SegmentsY+1) shared vertices.
/// The grid is the mesh you want for cloth/soft bodies: enough resolution to bend, and one shared
/// vertex per grid node so a soft-body constraint graph connects cleanly.
/// </summary>
public class PhosGrid : PhosShape
{
    public float2 Size = float2.One;
    public float2 UVScale = float2.One;

    public readonly int SegmentsX;
    public readonly int SegmentsY;

    public PhosVertex FirstVertex;

    private readonly int _cols;
    private readonly int _rows;
    private readonly int _totalVertices;

    public PhosGrid(PhosTriangleSubmesh submesh, int segmentsX, int segmentsY) : base(submesh.Mesh)
    {
        SegmentsX = System.Math.Max(1, segmentsX);
        SegmentsY = System.Math.Max(1, segmentsY);
        _cols = SegmentsX + 1;
        _rows = SegmentsY + 1;
        _totalVertices = _cols * _rows;

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        Mesh.IncreaseVertexCount(_totalVertices);
        FirstVertex = Mesh.GetVertex(Mesh.VertexCount - _totalVertices);
        int first = FirstVertex.IndexUnsafe;

        // Two triangles per cell.
        for (int y = 0; y < SegmentsY; y++)
        {
            for (int x = 0; x < SegmentsX; x++)
            {
                int v00 = first + y * _cols + x;
                int v10 = v00 + 1;
                int v01 = v00 + _cols;
                int v11 = v01 + 1;
                submesh.AddTriangle(v00, v01, v11);
                submesh.AddTriangle(v00, v11, v10);
            }
        }
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        int index = FirstVertex.Index;
        float2 half = Size * 0.5f;
        var normal = Rotation * float3.Backward;
        var tangent = new float4(Rotation * float3.Right, -1f);

        for (int y = 0; y < _rows; y++)
        {
            float fy = (float)y / SegmentsY;
            for (int x = 0; x < _cols; x++)
            {
                float fx = (float)x / SegmentsX;
                var local = new float3((fx - 0.5f) * Size.x, (fy - 0.5f) * Size.y, 0f);
                Mesh.RawPositions[index] = Rotation * local + Position;
                Mesh.RawNormals[index] = normal;
                Mesh.RawTangents[index] = tangent;
                Mesh.RawUV0s[index] = new float2(fx * UVScale.x, fy * UVScale.y);
                index++;
            }
        }
    }
}
