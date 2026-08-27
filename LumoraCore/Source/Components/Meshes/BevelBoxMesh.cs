// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// A plain box goes dead flat under any light because nothing on it ever catches a highlight; a bevel
// of a couple of millimetres gives every edge a specular line. Size is the full outer size including
// the bevel, so widening the bevel eats inward and never grows the box.
[ComponentCategory("Assets/Procedural Meshes")]
public class BevelBoxMesh : ProceduralMesh
{
    // bevel included
    public readonly Sync<float3> Size;

    // clamped to half the smallest dimension
    public readonly Sync<float> Bevel;

    // 1 gives a flat chamfer
    [Range(1f, PhosBevelBox.MaxBevelSegments, "0")]
    public readonly Sync<int> BevelSegments;

    public readonly Sync<float2> UVScale;

    private PhosBevelBox? _box;
    private float3 _size;
    private float _bevel;
    private int _bevelSegments;
    private float2 _uvScale;

    public BevelBoxMesh()
    {
        Size = new Sync<float3>(this, float3.One);
        Bevel = new Sync<float>(this, 0.05f);
        BevelSegments = new Sync<int>(this, 3);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Size);
        SubscribeToChanges(Bevel);
        SubscribeToChanges(BevelSegments);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _size = Size.Value;
        _bevel = Bevel.Value;
        _bevelSegments = System.Math.Clamp(BevelSegments.Value, 1, PhosBevelBox.MaxBevelSegments);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _box != null && _box.BevelSegments != _bevelSegments;
        uploadHint[MeshUploadHint.Flag.Geometry] = _box == null || rebuild;

        if (_box == null || rebuild)
        {
            if (_box != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _box = new PhosBevelBox(submesh, _bevelSegments);
        }

        _box.Size = _size;
        _box.Bevel = _bevel;
        _box.UVScale = _uvScale;
        _box.Update();
    }

    protected override void ClearMeshData()
    {
        _box = null;
    }
}
