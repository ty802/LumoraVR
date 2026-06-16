// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos.Collections;

namespace Lumora.Core.Phos;

/// <summary>
/// Procedural conical-frustum mesh generator: a radial shape with separate base and top radii
/// (a true cone when RadiusTop is 0), with top and bottom caps. Modelled on <see cref="PhosCylinder"/>
/// with slope-adjusted side normals so the slanted surface shades correctly.
/// </summary>
public class PhosCone : PhosShape
{
    /// <summary>First vertex of this cone</summary>
    public PhosVertex FirstVertex;

    /// <summary>Radius at the bottom (base) ring</summary>
    public float RadiusBase = 0.5f;

    /// <summary>Radius at the top ring (0 = a true cone tapering to a point)</summary>
    public float RadiusTop = 0f;

    /// <summary>Height of the cone</summary>
    public float Height = 1f;

    /// <summary>Number of radial segments</summary>
    public int Segments = 32;

    /// <summary>UV scale for texture mapping</summary>
    public float2 UVScale = new float2(1f, 1f);

    /// <summary>Optional solid color</summary>
    public color? Color;

    private int _segments;
    private int _totalVertices;
    private int _totalTriangles;

    public PhosCone(PhosTriangleSubmesh submesh, int segments = 32) : base(submesh.Mesh)
    {
        Segments = segments;
        _segments = segments;
        CalculateCounts();

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddConeGeometry(submesh);
    }

    private void CalculateCounts()
    {
        // Same layout as the cylinder: bottom+top side rings, then top and bottom caps.
        _totalVertices = (_segments + 1) * 2 + (_segments + 2) * 2;
        _totalTriangles = _segments * 2 + _segments * 2;
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateConeVertices();
    }

    private PhosVertex AddConeGeometry(PhosTriangleSubmesh submesh)
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

        ConnectTriangles(firstVertexIndex, firstTriangle, submesh);
        return firstVertex;
    }

    private void ConnectTriangles(int firstVertex, int firstTriangle, PhosTriangleSubmesh submesh)
    {
        int triIndex = firstTriangle;

        // Side triangles (bottom ring -> top ring)
        int sideStart = firstVertex;
        for (int i = 0; i < _segments; i++)
        {
            int bl = sideStart + i;
            int br = sideStart + i + 1;
            int tl = sideStart + (_segments + 1) + i;
            int tr = sideStart + (_segments + 1) + i + 1;

            submesh.SetTriangle(triIndex++, bl, br, tr);
            submesh.SetTriangle(triIndex++, bl, tr, tl);
        }

        // Top cap (winding reversed for correct face direction)
        int topCenterIndex = firstVertex + (_segments + 1) * 2;
        int topRingStart = topCenterIndex + 1;
        for (int i = 0; i < _segments; i++)
        {
            int v1 = topRingStart + i;
            int v2 = topRingStart + i + 1;
            submesh.SetTriangle(triIndex++, topCenterIndex, v1, v2);
        }

        // Bottom cap
        int bottomCenterIndex = topCenterIndex + _segments + 2;
        int bottomRingStart = bottomCenterIndex + 1;
        for (int i = 0; i < _segments; i++)
        {
            int v1 = bottomRingStart + i;
            int v2 = bottomRingStart + i + 1;
            submesh.SetTriangle(triIndex++, bottomCenterIndex, v2, v1);
        }
    }

    private void UpdateConeVertices()
    {
        int index = FirstVertex.Index;
        float halfHeight = Height / 2f;

        // Outward side normal in the (radial, y) plane, tilted by the cone's slope. For a cylinder
        // (dr = 0) this is purely radial; as the top narrows the normal tilts upward.
        float dr = RadiusTop - RadiusBase;
        float slopeLen = MathF.Sqrt(Height * Height + dr * dr);
        float nr = slopeLen > 0f ? Height / slopeLen : 1f;
        float ny = slopeLen > 0f ? -dr / slopeLen : 0f;

        // Side vertices - bottom ring
        for (int i = 0; i <= _segments; i++)
        {
            float angle = (float)i / _segments * MathF.PI * 2f;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            float3 pos = new float3(cos * RadiusBase, -halfHeight, sin * RadiusBase) + Position;
            float3 normal = new float3(cos * nr, ny, sin * nr);
            float4 tangent = new float4(-sin, 0f, cos, 1f);
            WriteVertex(index++, pos, normal, tangent, new float2((float)i / _segments * UVScale.x, 0f));
        }

        // Side vertices - top ring
        for (int i = 0; i <= _segments; i++)
        {
            float angle = (float)i / _segments * MathF.PI * 2f;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            float3 pos = new float3(cos * RadiusTop, halfHeight, sin * RadiusTop) + Position;
            float3 normal = new float3(cos * nr, ny, sin * nr);
            float4 tangent = new float4(-sin, 0f, cos, 1f);
            WriteVertex(index++, pos, normal, tangent, new float2((float)i / _segments * UVScale.x, UVScale.y));
        }

        // Top cap
        float topInvDiameter = RadiusTop > 0f ? 1f / (RadiusTop * 2f) : 0f;
        WriteVertex(index++, new float3(0f, halfHeight, 0f) + Position, float3.Up, new float4(1f, 0f, 0f, 1f),
            new float2(0.5f * UVScale.x, 0.5f * UVScale.y));
        for (int i = 0; i <= _segments; i++)
        {
            float angle = (float)i / _segments * MathF.PI * 2f;
            float x = MathF.Cos(angle) * RadiusTop, z = MathF.Sin(angle) * RadiusTop;
            WriteVertex(index++, new float3(x, halfHeight, z) + Position, float3.Up, new float4(1f, 0f, 0f, 1f),
                new float2((x * topInvDiameter + 0.5f) * UVScale.x, (z * topInvDiameter + 0.5f) * UVScale.y));
        }

        // Bottom cap
        float bottomInvDiameter = RadiusBase > 0f ? 1f / (RadiusBase * 2f) : 0f;
        WriteVertex(index++, new float3(0f, -halfHeight, 0f) + Position, float3.Down, new float4(1f, 0f, 0f, 1f),
            new float2(0.5f * UVScale.x, 0.5f * UVScale.y));
        for (int i = 0; i <= _segments; i++)
        {
            float angle = (float)i / _segments * MathF.PI * 2f;
            float x = MathF.Cos(angle) * RadiusBase, z = MathF.Sin(angle) * RadiusBase;
            WriteVertex(index++, new float3(x, -halfHeight, z) + Position, float3.Down, new float4(1f, 0f, 0f, 1f),
                new float2((x * bottomInvDiameter + 0.5f) * UVScale.x, (z * bottomInvDiameter + 0.5f) * UVScale.y));
        }
    }

    private void WriteVertex(int index, float3 pos, float3 normal, float4 tangent, float2 uv)
    {
        Mesh.RawPositions[index] = pos;
        Mesh.RawNormals[index] = normal;
        Mesh.RawTangents[index] = tangent;
        Mesh.RawUV0s[index] = uv;
        if (Color.HasValue)
            Mesh.RawColors[index] = Color.Value;
    }
}
