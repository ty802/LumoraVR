// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// Evenly sized near-equilateral triangles with no pinched poles, which is what makes it the better
// sphere for soft bodies, physics proxies and flat-shaded low-poly looks. Triangle count is
// 20 * 4^Subdivisions, so the level is capped.
[ComponentCategory("Assets/Procedural Meshes")]
public class IcoSphereMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;

    // each step quadruples the triangle count
    [Range(0f, PhosIcoSphere.MaxSubdivisions, "0")]
    public readonly Sync<int> Subdivisions;

    public readonly Sync<bool> FlatShading;

    public readonly Sync<float2> UVScale;

    private PhosIcoSphere? _sphere;
    private float _radius;
    private int _subdivisions;
    private bool _flatShading;
    private float2 _uvScale;

    public IcoSphereMesh()
    {
        Radius = new Sync<float>(this, 0.5f);
        Subdivisions = new Sync<int>(this, 2);
        FlatShading = new Sync<bool>(this, false);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Radius);
        SubscribeToChanges(Subdivisions);
        SubscribeToChanges(FlatShading);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = Radius.Value;
        _subdivisions = System.Math.Clamp(Subdivisions.Value, 0, PhosIcoSphere.MaxSubdivisions);
        _flatShading = FlatShading.Value;
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Only the subdivision level changes the vertex count; flat shading just swaps which normals
        // get written, because the triangles are unwelded either way.
        bool rebuild = _sphere != null && _sphere.Subdivisions != _subdivisions;
        uploadHint[MeshUploadHint.Flag.Geometry] = _sphere == null || rebuild;

        if (_sphere == null || rebuild)
        {
            if (_sphere != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _sphere = new PhosIcoSphere(submesh, _subdivisions);
        }

        _sphere.Radius = _radius;
        _sphere.FlatShading = _flatShading;
        _sphere.UVScale = _uvScale;
        _sphere.Update();
    }

    protected override void ClearMeshData()
    {
        _sphere = null;
    }
}
