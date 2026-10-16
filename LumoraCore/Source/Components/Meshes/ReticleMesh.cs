// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// A flat XY reticle. Geometry leaves the ring's centre empty; it needs no texture or font to render.
[ComponentCategory("Hidden")]
public sealed class ReticleMesh : ProceduralMesh
{
    public readonly Sync<EngineSettings.ReticleShape> Shape;
    public readonly Sync<float> Radius;
    public readonly Sync<float> Thickness;

    private EngineSettings.ReticleShape _shape;
    private float _radius;
    private float _thickness;
    private const int Segments = 64;

    public ReticleMesh()
    {
        Shape = new(this, EngineSettings.ReticleShape.Ring);
        Radius = new(this, 12f);
        Thickness = new(this, 2f);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(Shape);
        SubscribeToChanges(Radius);
        SubscribeToChanges(Thickness);
    }

    protected override void PrepareAssetUpdateData()
    {
        _shape = Shape.Value;
        _radius = MathF.Max(0.01f, float.IsFinite(Radius.Value) ? Radius.Value : 12f);
        _thickness = MathF.Max(0.01f, float.IsFinite(Thickness.Value) ? Thickness.Value : 2f);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        mesh.Clear();
        uploadHint.SetAll();
        if (_shape == EngineSettings.ReticleShape.Off)
            return;

        mesh.HasNormals = true;
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
        var triangles = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(triangles);

        if (_shape == EngineSettings.ReticleShape.Crosshair)
        {
            // Split the vertical arm around the horizontal one, so their alpha never doubles up.
            float half = _thickness * 0.5f;
            float extent = MathF.Max(_radius, half);
            mesh.IncreaseVertexCount(12);
            triangles.SetCount(6);
            Quad(0, -extent, -half, extent, half);
            Quad(4, -half, -extent, half, -half);
            Quad(8, -half, half, half, extent);
            return;
        }

        float outer = _shape == EngineSettings.ReticleShape.Dot
            ? MathF.Max(_thickness, _radius * 0.25f) : _radius + _thickness * 0.5f;
        float inner = _shape == EngineSettings.ReticleShape.Dot
            ? 0f : MathF.Max(0f, _radius - _thickness * 0.5f);
        bool disc = inner == 0f;
        mesh.IncreaseVertexCount(disc ? Segments + 1 : Segments * 2);
        triangles.SetCount(disc ? Segments : Segments * 2);
        if (disc) Vertex(Segments, 0f, 0f);
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * MathF.Tau / Segments;
            float x = MathF.Cos(angle), y = MathF.Sin(angle);
            Vertex(i, x * outer, y * outer);
            int next = (i + 1) % Segments;
            if (disc)
                triangles.SetTriangle(i, Segments, next, i);
            else
            {
                Vertex(i + Segments, x * inner, y * inner);
                triangles.SetTriangle(i * 2, i, next + Segments, next);
                triangles.SetTriangle(i * 2 + 1, i, i + Segments, next + Segments);
            }
        }

        void Vertex(int index, float x, float y)
        {
            mesh.RawPositions[index] = new float3(x, y, 0f);
            mesh.RawNormals[index] = new float3(0f, 0f, -1f);
            mesh.RawColors[index] = color.White;
            mesh.RawUV0s[index] = new float2(x / (_radius * 2f) + 0.5f, y / (_radius * 2f) + 0.5f);
        }

        void Quad(int index, float left, float bottom, float right, float top)
        {
            Vertex(index, left, bottom);
            Vertex(index + 1, right, bottom);
            Vertex(index + 2, right, top);
            Vertex(index + 3, left, top);
            triangles.SetTriangle(index / 2, index, index + 2, index + 1);
            triangles.SetTriangle(index / 2 + 1, index, index + 3, index + 2);
        }
    }

    protected override void ClearMeshData() { }
}
