// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

/// <summary>
/// A mesh whose vertex positions are written externally each frame (by a soft-body sim). Topology is
/// set once via SetGeometry; UpdatePositions pushes new positions and re-uploads. Normals recompute
/// from the current positions so lighting follows the deformation.
/// </summary>
public class DeformableMesh : ProceduralMesh
{
    private float3[] _positions = System.Array.Empty<float3>();
    private float3[] _normals = System.Array.Empty<float3>();
    private float2[] _uvs = System.Array.Empty<float2>();
    private int[] _indices = System.Array.Empty<int>();
    private bool _hasGeometry;

    /// <summary>Set the fixed topology + initial positions (local space).</summary>
    public void SetGeometry(float3[] positions, int[] indices, float2[]? uvs)
    {
        _positions = positions;
        _indices = indices;
        _uvs = uvs ?? System.Array.Empty<float2>();
        _normals = new float3[positions.Length];
        RecomputeNormals();
        _hasGeometry = true;
        if (phosMesh != null)
            RegenerateMesh();
    }

    /// <summary>Push new vertex positions (local space) and re-upload.</summary>
    public void UpdatePositions(float3[] positions)
    {
        if (!_hasGeometry || positions.Length != _positions.Length)
            return;
        System.Array.Copy(positions, _positions, positions.Length);
        RecomputeNormals();
        RegenerateMesh();
    }

    private void RecomputeNormals()
    {
        for (int i = 0; i < _normals.Length; i++)
            _normals[i] = float3.Zero;
        for (int t = 0; t + 2 < _indices.Length; t += 3)
        {
            int a = _indices[t], b = _indices[t + 1], c = _indices[t + 2];
            var n = float3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
            _normals[a] += n; _normals[b] += n; _normals[c] += n;
        }
        for (int i = 0; i < _normals.Length; i++)
            _normals[i] = _normals[i].LengthSquared > 1e-10f ? _normals[i].Normalized : float3.Up;
    }

    protected override void PrepareAssetUpdateData() { }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        if (!_hasGeometry || _positions.Length == 0)
        {
            mesh.Clear();
            return;
        }

        // Topology (vertex count, indices, UVs) is fixed for a soft body; only positions/normals change
        // each frame. Rebuild the PhosMesh only when the count actually changes - otherwise Clear() drops
        // every backing array and IncreaseVertexCount reallocates them + the index list per frame, which
        // is needless GC for a mesh that's just deforming. Rewrite vertex data in place instead. -xlinka
        bool rebuild = mesh.VertexCount != _positions.Length || mesh.Submeshes.Count == 0;
        if (rebuild)
        {
            mesh.Clear();
            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            mesh.HasNormals = true;
            mesh.HasUV0s = _uvs.Length == _positions.Length;
            mesh.IncreaseVertexCount(_positions.Length);
            for (int t = 0; t + 2 < _indices.Length; t += 3)
                submesh.AddTriangle(_indices[t], _indices[t + 1], _indices[t + 2]);
        }

        bool hasUV = mesh.HasUV0s;
        for (int i = 0; i < _positions.Length; i++)
        {
            mesh.RawPositions[i] = _positions[i];
            mesh.RawNormals[i] = _normals[i];
            if (hasUV && rebuild)
                mesh.RawUV0s[i] = _uvs[i]; // UVs never change after the topology is built
        }
    }

    protected override void ClearMeshData()
    {
        _hasGeometry = false;
    }
}
