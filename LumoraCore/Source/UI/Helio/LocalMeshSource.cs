// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Phos;

namespace Helio.UI;

[ComponentCategory("Hidden")]
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

    // Whole channels at a time: a heavy panel's submit was copying its canvas mesh vertex by vertex.
    private static void CopyVertices(PhosMesh source, PhosMesh target)
    {
        int count = source.VertexCount;
        Array.Copy(source.RawPositions, target.RawPositions, count);
        if (source.HasNormals)
            Array.Copy(source.RawNormals, target.RawNormals, count);
        if (source.HasTangents)
            Array.Copy(source.RawTangents, target.RawTangents, count);
        if (source.HasColors)
            Array.Copy(source.RawColors, target.RawColors, count);
        if (source.HasUV0s)
            Array.Copy(source.RawUV0s, target.RawUV0s, count);
        if (source.UVChannelCount > 1 && source.RawUV1s.Length >= count)
            Array.Copy(source.RawUV1s, target.RawUV1s, count);
        if (source.UVChannelCount > 2 && source.RawUV2s.Length >= count)
            Array.Copy(source.RawUV2s, target.RawUV2s, count);
        if (source.UVChannelCount > 3 && source.RawUV3s.Length >= count)
            Array.Copy(source.RawUV3s, target.RawUV3s, count);
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

            int triangles = sourceTriangleSubmesh.Count;
            if (triangles == 0)
                continue;
            targetTriangleSubmesh.IncreaseCount(triangles);
            Array.Copy(sourceTriangleSubmesh.RawIndices, targetTriangleSubmesh.RawIndices, sourceTriangleSubmesh.IndexCount);
        }
    }
}
