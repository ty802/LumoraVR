// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

[ComponentCategory("Assets/Procedural Meshes")]
public class SegmentMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;
    public readonly Sync<int> Sides;
    public readonly Sync<float3> PointA;
    public readonly Sync<float3> PointB;
    public readonly Sync<color> PointAColor;
    public readonly Sync<color> PointBColor;

    private PhosTriangleSubmesh? _submesh;
    private float _radius;
    private int _sides;
    private float3 _pointA;
    private float3 _pointB;
    private color _pointAColor;
    private color _pointBColor;
    private int _lastVertexCount;
    private int _lastTriangleCount;

    public SegmentMesh()
    {
        Radius = new Sync<float>(this, 0.01f);
        Sides = new Sync<int>(this, 6);
        PointA = new Sync<float3>(this, float3.Zero);
        PointB = new Sync<float3>(this, float3.Backward);
        PointAColor = new Sync<color>(this, color.White);
        PointBColor = new Sync<color>(this, color.White);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(Radius);
        SubscribeToChanges(Sides);
        SubscribeToChanges(PointA);
        SubscribeToChanges(PointB);
        SubscribeToChanges(PointAColor);
        SubscribeToChanges(PointBColor);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = MathF.Max(0.0001f, Radius.Value);
        _sides = System.Math.Clamp(Sides.Value, 3, 64);
        _pointA = FilterInvalid(PointA.Value);
        _pointB = FilterInvalid(PointB.Value);
        _pointAColor = PointAColor.Value;
        _pointBColor = PointBColor.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int vertexCount = _sides * 2 + 2;
        int triangleCount = _sides * 4;
        bool topologyChanged = _submesh == null ||
                               _lastVertexCount != vertexCount ||
                               _lastTriangleCount != triangleCount;

        if (topologyChanged)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.HasColors = true;
            mesh.SetHasUV(0, true);
            mesh.IncreaseVertexCount(vertexCount);

            _submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(_submesh);
            _submesh.SetCount(triangleCount);
            _lastVertexCount = vertexCount;
            _lastTriangleCount = triangleCount;
        }
        else
        {
            mesh.HasNormals = true;
            mesh.HasColors = true;
            mesh.SetHasUV(0, true);
        }

        BuildSegment(mesh, _submesh!);
        uploadHint[MeshUploadHint.Flag.Geometry] = topologyChanged;
        uploadHint[MeshUploadHint.Flag.Normals] = true;
        uploadHint[MeshUploadHint.Flag.Colors] = true;
        uploadHint[MeshUploadHint.Flag.UV0] = true;
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
        _lastVertexCount = 0;
        _lastTriangleCount = 0;
    }

    private void BuildSegment(PhosMesh mesh, PhosTriangleSubmesh submesh)
    {
        float3 axis = _pointB - _pointA;
        float3 tangent = axis.Length > 0.0001f ? axis.Normalized : float3.Backward;
        BuildFrame(tangent, out float3 normal, out float3 binormal);

        for (int side = 0; side < _sides; side++)
        {
            float angle = MathF.PI * 2f * side / _sides;
            float s = MathF.Cos(angle);
            float c = MathF.Sin(angle);
            float3 radial = normal * c + binormal * s;
            int aIndex = side;
            int bIndex = _sides + side;

            mesh.RawPositions[aIndex] = _pointA + radial * _radius;
            mesh.RawPositions[bIndex] = _pointB + radial * _radius;
            mesh.RawNormals[aIndex] = radial;
            mesh.RawNormals[bIndex] = radial;
            mesh.RawColors[aIndex] = _pointAColor;
            mesh.RawColors[bIndex] = _pointBColor;
            mesh.RawUV0s[aIndex] = new float2(side / (float)_sides, 0f);
            mesh.RawUV0s[bIndex] = new float2(side / (float)_sides, 1f);
        }

        int capA = _sides * 2;
        int capB = capA + 1;
        mesh.RawPositions[capA] = _pointA;
        mesh.RawPositions[capB] = _pointB;
        mesh.RawNormals[capA] = -tangent;
        mesh.RawNormals[capB] = tangent;
        mesh.RawColors[capA] = _pointAColor;
        mesh.RawColors[capB] = _pointBColor;
        mesh.RawUV0s[capA] = new float2(0.5f, 0f);
        mesh.RawUV0s[capB] = new float2(0.5f, 1f);

        int triangle = 0;
        for (int side = 0; side < _sides; side++)
        {
            int nextSide = (side + 1) % _sides;
            int a0 = side;
            int a1 = nextSide;
            int b0 = _sides + side;
            int b1 = _sides + nextSide;

            submesh.SetTriangle(triangle++, a0, b0, a1);
            submesh.SetTriangle(triangle++, a1, b0, b1);
            submesh.SetTriangle(triangle++, capA, a1, a0);
            submesh.SetTriangle(triangle++, capB, b0, b1);
        }
    }

    private static void BuildFrame(float3 tangent, out float3 normal, out float3 binormal)
    {
        float3 up = MathF.Abs(float3.Dot(tangent, float3.Up)) > 0.95f ? float3.Right : float3.Up;
        normal = float3.Cross(up, tangent);
        normal = normal.Length > 0.0001f ? normal.Normalized : float3.Right;
        binormal = float3.Cross(tangent, normal);
        binormal = binormal.Length > 0.0001f ? binormal.Normalized : float3.Up;
    }

    private static float3 FilterInvalid(float3 value)
    {
        return new float3(
            float.IsFinite(value.x) ? value.x : 0f,
            float.IsFinite(value.y) ? value.y : 0f,
            float.IsFinite(value.z) ? value.z : 0f);
    }
}
