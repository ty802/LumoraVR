// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Scripting;

// Small bright beads in a graph's own space: flow moving along its wires, or the port a wire end is
// pulled onto. One mesh for all of them, rebuilt only while any moves. Local, never synced. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptPulseMesh : ProceduralMesh
{
    // Six points and eight faces: round enough at the size of a bead.
    private static readonly float3[] Corners =
    {
        new(1f, 0f, 0f), new(-1f, 0f, 0f), new(0f, 1f, 0f), new(0f, -1f, 0f), new(0f, 0f, 1f), new(0f, 0f, -1f),
    };

    private static readonly int[] Faces =
    {
        0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5,
    };

    private readonly List<(float3 Center, float Radius, color Tint)> _beads = new();
    private (float3 Center, float Radius, color Tint)[] _building = Array.Empty<(float3, float, color)>();
    private PhosTriangleSubmesh? _submesh;
    private int _lastCount = -1;

    public int BeadCount => _beads.Count;

    public void Clear()
    {
        if (_beads.Count == 0 && _lastCount == 0)
            return;
        _beads.Clear();
        RegenerateMesh();
    }

    public void SetBeads(IReadOnlyList<(float3 Center, float Radius, color Tint)> beads)
    {
        _beads.Clear();
        for (int i = 0; i < beads.Count; i++)
            _beads.Add(beads[i]);
        RegenerateMesh();
    }

    protected override void PrepareAssetUpdateData()
    {
        if (_building.Length != _beads.Count)
            _building = new (float3, float, color)[_beads.Count];
        _beads.CopyTo(_building);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int beads = _building.Length;
        bool topologyChanged = _submesh == null || beads != _lastCount;
        if (topologyChanged)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.HasColors = true;
            mesh.IncreaseVertexCount(beads * Faces.Length);
            _submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(_submesh);
            _submesh.SetCount(beads * Faces.Length / 3);
            _lastCount = beads;
        }
        mesh.HasNormals = true;
        mesh.HasColors = true;

        int vertex = 0;
        int triangle = 0;
        foreach (var (center, radius, tint) in _building)
        {
            for (int face = 0; face < Faces.Length; face += 3)
            {
                var a = Corners[Faces[face]];
                var b = Corners[Faces[face + 1]];
                var c = Corners[Faces[face + 2]];
                var normal = (a + b + c).Normalized;
                int first = vertex;
                Put(mesh, ref vertex, center + a * radius, normal, tint);
                Put(mesh, ref vertex, center + b * radius, normal, tint);
                Put(mesh, ref vertex, center + c * radius, normal, tint);
                _submesh!.SetTriangle(triangle++, first, first + 1, first + 2);
            }
        }

        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        uploadHint[MeshUploadHint.Flag.Normals] = true;
        uploadHint[MeshUploadHint.Flag.Colors] = true;
    }

    private static void Put(PhosMesh mesh, ref int vertex, in float3 position, in float3 normal, in color tint)
    {
        mesh.RawPositions[vertex] = position;
        mesh.RawNormals[vertex] = normal;
        mesh.RawColors[vertex] = tint;
        vertex++;
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
        _lastCount = -1;
    }
}
