// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Scripting;

// Every card's slab of a graph as one mesh, so the slabs behind a graph's cards are one draw however many
// cards it has. Each card carried a slab mesh and a renderer of its own, a draw call and a few Godot nodes
// a card, and a converted avatar opens a thousand cards. Built on the machine that draws it from a list the
// graph's view hands in, never synced. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptCardBackings : ProceduralMesh
{
    private const int CornerSegments = 8;
    private const int Perimeter = (CornerSegments + 1) * 4;

    private readonly List<ScriptCardSlab> _slabs = new();
    private ScriptCardSlab[] _building = Array.Empty<ScriptCardSlab>();
    private PhosTriangleSubmesh? _submesh;
    private int _lastCount = -1;

    public int SlabCount => _slabs.Count;

    public void SetSlabs(IReadOnlyList<ScriptCardSlab> slabs)
    {
        _slabs.Clear();
        for (int i = 0; i < slabs.Count; i++)
            _slabs.Add(slabs[i]);
        RegenerateMesh();
    }

    protected override void PrepareAssetUpdateData()
    {
        if (_building.Length != _slabs.Count)
            _building = new ScriptCardSlab[_slabs.Count];
        _slabs.CopyTo(_building);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int count = _building.Length;
        if (_submesh == null || count != _lastCount)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.IncreaseVertexCount(count * (Perimeter + 1));
            _submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(_submesh);
            _submesh.SetCount(count * Perimeter);
            _lastCount = count;
        }
        mesh.HasNormals = true;

        int vertex = 0;
        int triangle = 0;
        foreach (var slab in _building)
            BuildSlab(mesh, _submesh!, slab, ref vertex, ref triangle);

        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        uploadHint[MeshUploadHint.Flag.Normals] = true;
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
        _lastCount = -1;
    }

    // A rounded rectangle as a fan round its middle: a corner of CornerSegments steps at each of the four
    // corners, going round counter-clockwise from the top right.
    private static void BuildSlab(PhosMesh mesh, PhosTriangleSubmesh submesh, in ScriptCardSlab slab, ref int vertex, ref int triangle)
    {
        float halfWidth = MathF.Max(0f, slab.Size.x * 0.5f);
        float halfHeight = MathF.Max(0f, slab.Size.y * 0.5f);
        float radius = MathF.Min(MathF.Max(0f, slab.CornerRadius), MathF.Min(halfWidth, halfHeight));
        var normal = slab.Rotation * float3.Forward;

        int center = vertex;
        mesh.RawPositions[vertex] = slab.Center;
        mesh.RawNormals[vertex] = normal;
        vertex++;

        for (int corner = 0; corner < 4; corner++)
        {
            float cornerX = corner is 0 or 3 ? halfWidth - radius : -halfWidth + radius;
            float cornerY = corner is 0 or 1 ? halfHeight - radius : -halfHeight + radius;
            float startAngle = corner * MathF.PI * 0.5f;
            for (int step = 0; step <= CornerSegments; step++)
            {
                float angle = startAngle + step / (float)CornerSegments * MathF.PI * 0.5f;
                var local = new float3(cornerX + MathF.Cos(angle) * radius, cornerY + MathF.Sin(angle) * radius, 0f);
                mesh.RawPositions[vertex] = slab.Center + slab.Rotation * local;
                mesh.RawNormals[vertex] = normal;
                vertex++;
            }
        }

        for (int i = 0; i < Perimeter; i++)
        {
            int a = center + 1 + i;
            int b = center + 1 + (i + 1) % Perimeter;
            submesh.SetTriangle(triangle++, center, a, b);
        }
    }
}
