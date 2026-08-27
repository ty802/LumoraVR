// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// Flat faces with rounded edges and corners, which is what makes a box catch a highlight along its
// edges instead of reading as a flat-shaded slab.
//
// Built as a sphere of radius Bevel whose vertices are pushed out to the eight corners of the inner
// box: every surface point of a rounded box is a sphere point offset by one of eight corner vectors,
// so one latitude/longitude grid produces the corners, the twelve edge rounds and the four side faces
// at once. The trick is the seams. Each of the four longitude quadrants and each of the two latitude
// hemispheres ends with a duplicated row or column carrying the neighbouring octant's offset, and the
// flat side faces are exactly the bands between those duplicates.
//
// The two poles are the exception: a flat top and bottom face need a 2D patch, and a pole is a single
// point in the grid, so those two get a centre vertex and a fan like a cylinder cap. Fan triangles
// between columns that share an octant come out zero-area and cost nothing. -xlinka
public class PhosBevelBox : PhosShape
{
    public const int MaxBevelSegments = 16;

    public PhosVertex FirstVertex;

    public float3 Size = float3.One;

    // clamped to half the smallest dimension
    public float Bevel = 0.05f;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int BevelSegments;

    private readonly int _rows;
    private readonly int _cols;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Per-column longitude and the octant sign it belongs to; per-row latitude and its sign.
    private readonly float[] _colAngle;
    private readonly float[] _colSignX;
    private readonly float[] _colSignZ;
    private readonly float[] _rowAngle;
    private readonly float[] _rowSignY;

    // Constructors

    public PhosBevelBox(PhosTriangleSubmesh submesh, int bevelSegments = 3) : base(submesh.Mesh)
    {
        BevelSegments = System.Math.Clamp(bevelSegments, 1, MaxBevelSegments);

        int bs = BevelSegments;
        _cols = 4 * (bs + 1) + 1;
        _rows = 2 * (bs + 1);
        _totalVertices = _rows * _cols + 2;
        _totalTriangles = (_rows - 1) * (_cols - 1) * 2 + 2 * (_cols - 1);

        _colAngle = new float[_cols];
        _colSignX = new float[_cols];
        _colSignZ = new float[_cols];
        _rowAngle = new float[_rows];
        _rowSignY = new float[_rows];
        BuildParameterization();

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddBoxGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateBoxVertices();
    }

    private int BottomCenter => FirstVertex.Index + _rows * _cols;
    private int TopCenter => BottomCenter + 1;

    private void BuildParameterization()
    {
        int bs = BevelSegments;
        float quarter = MathF.PI * 0.5f;

        int c = 0;
        for (int q = 0; q < 4; q++)
        {
            // Quadrant 0 spans +X+Z, 1 spans -X+Z, 2 spans -X-Z, 3 spans +X-Z. Both ends of every
            // quadrant are written, so each quadrant boundary gets two columns with different signs.
            float sx = (q == 0 || q == 3) ? 1f : -1f;
            float sz = (q == 0 || q == 1) ? 1f : -1f;
            for (int k = 0; k <= bs; k++)
            {
                _colAngle[c] = (q + (float)k / bs) * quarter;
                _colSignX[c] = sx;
                _colSignZ[c] = sz;
                c++;
            }
        }
        // Closing column: same place as the last quadrant's end, first quadrant's octant. The band
        // between the two is the +X face.
        _colAngle[c] = MathF.PI * 2f;
        _colSignX[c] = 1f;
        _colSignZ[c] = 1f;

        int r = 0;
        for (int h = 0; h < 2; h++)
        {
            float sy = h == 0 ? -1f : 1f;
            for (int k = 0; k <= bs; k++)
            {
                _rowAngle[r] = -quarter + h * quarter + (float)k / bs * quarter;
                _rowSignY[r] = sy;
                r++;
            }
        }
    }

    private PhosVertex AddBoxGeometry(PhosTriangleSubmesh submesh)
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
            for (int col = 0; col < _cols - 1; col++)
            {
                int bl = firstVertexIndex + row * _cols + col;
                int br = bl + 1;
                int tl = bl + _cols;
                int tr = tl + 1;
                submesh.SetTriangle(triIndex++, bl, br, tr);
                submesh.SetTriangle(triIndex++, bl, tr, tl);
            }
        }

        int bottomCenter = firstVertexIndex + _rows * _cols;
        int topCenter = bottomCenter + 1;
        int bottomRow = firstVertexIndex;
        int topRow = firstVertexIndex + (_rows - 1) * _cols;
        for (int col = 0; col < _cols - 1; col++)
        {
            submesh.SetTriangle(triIndex++, bottomCenter, bottomRow + col + 1, bottomRow + col);
            submesh.SetTriangle(triIndex++, topCenter, topRow + col, topRow + col + 1);
        }

        return firstVertex;
    }

    private void UpdateBoxVertices()
    {
        float3 half = Size * 0.5f;
        float minHalf = System.Math.Min(half.x, System.Math.Min(half.y, half.z));
        float bevel = System.Math.Clamp(Bevel, 0f, System.Math.Max(minHalf, 0f));
        float3 inner = new float3(
            System.Math.Max(half.x - bevel, 0f),
            System.Math.Max(half.y - bevel, 0f),
            System.Math.Max(half.z - bevel, 0f));

        int index = FirstVertex.Index;
        for (int row = 0; row < _rows; row++)
        {
            float lat = _rowAngle[row];
            float sinLat = MathF.Sin(lat);
            float cosLat = MathF.Cos(lat);
            float sy = _rowSignY[row];
            float v = (float)row / (_rows - 1) * UVScale.y;

            for (int col = 0; col < _cols; col++)
            {
                float lon = _colAngle[col];
                float cosLon = MathF.Cos(lon);
                float sinLon = MathF.Sin(lon);

                float3 normal = new float3(cosLat * cosLon, sinLat, cosLat * sinLon);
                float3 corner = new float3(_colSignX[col] * inner.x, sy * inner.y, _colSignZ[col] * inner.z);

                Mesh.RawPositions[index] = normal * bevel + corner + Position;
                Mesh.RawNormals[index] = normal;
                Mesh.RawTangents[index] = new float4(-sinLon, 0f, cosLon, 1f);
                Mesh.RawUV0s[index] = new float2((float)col / (_cols - 1) * UVScale.x, v);
                if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
                index++;
            }
        }

        WriteCapCenter(BottomCenter, new float3(0f, -half.y, 0f), float3.Down);
        WriteCapCenter(TopCenter, new float3(0f, half.y, 0f), float3.Up);
    }

    private void WriteCapCenter(int index, float3 localPosition, float3 normal)
    {
        Mesh.RawPositions[index] = localPosition + Position;
        Mesh.RawNormals[index] = normal;
        Mesh.RawTangents[index] = new float4(1f, 0f, 0f, 1f);
        Mesh.RawUV0s[index] = new float2(0.5f * UVScale.x, 0.5f * UVScale.y);
        if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
    }
}
