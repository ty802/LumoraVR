// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Meshes;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class LocalMeshSource : ProceduralMesh
{
    private PhosMesh? _source;

    public void SetMesh(PhosMesh mesh)
    {
        _source = mesh;
        RegenerateMesh();
    }

    protected override void PrepareAssetUpdateData()
    {
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        CopyMesh(_source, mesh);
    }

    protected override void ClearMeshData()
    {
        _source = null;
    }

    private static void CopyMesh(PhosMesh? source, PhosMesh target)
    {
        target.Clear();
        if (source == null)
        {
            return;
        }

        target.HasNormals = source.HasNormals;
        target.HasTangents = source.HasTangents;
        target.HasColors = source.HasColors;
        target.HasBoneBindings = source.HasBoneBindings;
        target.HasFlags = source.HasFlags;

        for (int channel = 0; channel < source.UVChannelCount; channel++)
        {
            target.SetHasUV(channel, true);
        }

        target.IncreaseVertexCount(source.VertexCount);

        CopyVertices(source, target);
        CopySubmeshes(source, target);
    }

    private static void CopyVertices(PhosMesh source, PhosMesh target)
    {
        for (int i = 0; i < source.VertexCount; i++)
        {
            target.RawPositions[i] = source.RawPositions[i];
        }

        if (source.HasNormals)
        {
            for (int i = 0; i < source.VertexCount; i++)
            {
                target.RawNormals[i] = source.RawNormals[i];
            }
        }

        if (source.HasTangents)
        {
            for (int i = 0; i < source.VertexCount; i++)
            {
                target.RawTangents[i] = source.RawTangents[i];
            }
        }

        if (source.HasColors)
        {
            for (int i = 0; i < source.VertexCount; i++)
            {
                target.RawColors[i] = source.RawColors[i];
            }
        }

        if (source.HasUV0s)
        {
            for (int i = 0; i < source.VertexCount; i++)
            {
                target.SetUV(0, i, source.RawUV0s[i]);
            }
        }
    }

    private static void CopySubmeshes(PhosMesh source, PhosMesh target)
    {
        foreach (var submesh in source.Submeshes)
        {
            if (submesh is not PhosTriangleSubmesh sourceTriangleSubmesh)
            {
                continue;
            }

            var targetTriangleSubmesh = new PhosTriangleSubmesh(target);
            target.Submeshes.Add(targetTriangleSubmesh);

            for (int i = 0; i < sourceTriangleSubmesh.Count; i++)
            {
                sourceTriangleSubmesh.GetIndices(i, out int v0, out int v1, out int v2);
                targetTriangleSubmesh.AddTriangle(v0, v1, v2);
            }
        }
    }
}
