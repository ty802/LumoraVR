// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Gizmos;

// All twelve edges in one surface, shared by the toolbar's cubes.
[ComponentCategory("Utility/Gizmos")]
public sealed class GizmoCubeOutlineMesh : ProceduralMesh
{
    protected override void PrepareAssetUpdateData() { }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        mesh.Clear();
        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);
        const float width = .025f;
        foreach (float a in new[] { -.5f, .5f })
        foreach (float b in new[] { -.5f, .5f })
        {
            Edge(new float3(0, a, b), new float3(1, width, width));
            Edge(new float3(a, 0, b), new float3(width, 1, width));
            Edge(new float3(a, b, 0), new float3(width, width, 1));
        }
        uploadHint[MeshUploadHint.Flag.Geometry] = true;

        void Edge(float3 position, float3 size)
            => new PhosBox(submesh) { Position = position, Size = size }.Update();
    }

    protected override void ClearMeshData() { }
}
