// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

/// <summary>
/// Procedural beam that eases from a direct target point to an actual target point.
/// Builds a curved interaction beam mesh without depending on platform-specific builders.
/// </summary>
[ComponentCategory("Assets/Procedural Meshes")]
public class CurvedBeamMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;
    public readonly Sync<int> Sides;
    public readonly Sync<int> Segments;
    public readonly Sync<float3> StartPoint;
    public readonly Sync<float3> DirectTargetPoint;
    public readonly Sync<float3> ActualTargetPoint;
    public readonly Sync<color> StartPointColor;
    public readonly Sync<color> EndPointColor;

    private PhosTriangleSubmesh? _submesh;
    private float _radius;
    private int _sides;
    private int _segments;
    private float3 _startPoint;
    private float3 _directTargetPoint;
    private float3 _actualTargetPoint;
    private color _startPointColor;
    private color _endPointColor;
    private int _lastVertexCount;
    private int _lastTriangleCount;

    public CurvedBeamMesh()
    {
        Radius = new Sync<float>(this, 0.01f);
        Sides = new Sync<int>(this, 6);
        Segments = new Sync<int>(this, 16);
        StartPoint = new Sync<float3>(this, float3.Zero);
        DirectTargetPoint = new Sync<float3>(this, float3.Forward);
        ActualTargetPoint = new Sync<float3>(this, float3.Forward + float3.Right);
        StartPointColor = new Sync<color>(this, color.White);
        EndPointColor = new Sync<color>(this, color.White);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Radius);
        SubscribeToChanges(Sides);
        SubscribeToChanges(Segments);
        SubscribeToChanges(StartPoint);
        SubscribeToChanges(DirectTargetPoint);
        SubscribeToChanges(ActualTargetPoint);
        SubscribeToChanges(StartPointColor);
        SubscribeToChanges(EndPointColor);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = MathF.Max(0.0001f, Radius.Value);
        _sides = System.Math.Clamp(Sides.Value, 3, 64);
        _segments = System.Math.Clamp(Segments.Value, 3, 256);
        _startPoint = FilterInvalid(StartPoint.Value);
        _directTargetPoint = FilterInvalid(DirectTargetPoint.Value);
        _actualTargetPoint = FilterInvalid(ActualTargetPoint.Value);
        _startPointColor = StartPointColor.Value;
        _endPointColor = EndPointColor.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int ringCount = _segments;
        int vertexCount = ringCount * _sides;
        int triangleCount = (ringCount - 1) * _sides * 2;
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

        BuildTube(mesh, _submesh!);
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

    private void BuildTube(PhosMesh mesh, PhosTriangleSubmesh submesh)
    {
        float invSegments = 1f / (_segments - 1);
        for (int i = 0; i < _segments; i++)
        {
            float t = i * invSegments;
            float3 center = GetPoint(t);
            float3 tangent = GetTangent(t);
            BuildFrame(tangent, out float3 normal, out float3 binormal);
            color vertexColor = color.Lerp(_startPointColor, _endPointColor, t);

            for (int side = 0; side < _sides; side++)
            {
                float angle = MathF.PI * 2f * side / _sides;
                float s = MathF.Cos(angle);
                float c = MathF.Sin(angle);
                float3 radial = normal * c + binormal * s;
                int index = i * _sides + side;

                mesh.RawPositions[index] = center + radial * _radius;
                mesh.RawNormals[index] = radial;
                mesh.RawColors[index] = vertexColor;
                mesh.RawUV0s[index] = new float2(side / (float)_sides, t);
            }
        }

        int triangle = 0;
        for (int i = 0; i < _segments - 1; i++)
        {
            int row = i * _sides;
            int nextRow = (i + 1) * _sides;
            for (int side = 0; side < _sides; side++)
            {
                int nextSide = (side + 1) % _sides;
                int v0 = row + side;
                int v1 = row + nextSide;
                int v2 = nextRow + side;
                int v3 = nextRow + nextSide;

                submesh.SetTriangle(triangle++, v0, v2, v1);
                submesh.SetTriangle(triangle++, v1, v2, v3);
            }
        }
    }

    private float3 GetPoint(float t)
    {
        float3 bendTarget = float3.Lerp(_directTargetPoint, _actualTargetPoint, t);
        return float3.Lerp(_startPoint, bendTarget, t);
    }

    private float3 GetTangent(float t)
    {
        const float step = 0.01f;
        float before = System.Math.Clamp(t - step, 0f, 1f);
        float after = System.Math.Clamp(t + step, 0f, 1f);
        float3 tangent = GetPoint(after) - GetPoint(before);
        return tangent.Length > 0.0001f ? tangent.Normalized : float3.Forward;
    }

    private static void BuildFrame(float3 tangent, out float3 normal, out float3 binormal)
    {
        float3 up = MathF.Abs(float3.Dot(tangent, float3.Up)) > 0.95f ? float3.Right : float3.Up;
        normal = float3.Cross(up, tangent);
        if (normal.Length < 0.0001f)
        {
            normal = float3.Right;
        }
        else
        {
            normal = normal.Normalized;
        }

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
