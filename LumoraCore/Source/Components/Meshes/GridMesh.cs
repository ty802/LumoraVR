// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

/// <summary>
/// Subdivided plane (grid) on the local XY plane. Segments control the cloth/soft-body resolution.
/// </summary>
public class GridMesh : ProceduralMesh
{
    public readonly Sync<float2> Size;
    public readonly Sync<int> SegmentsX;
    public readonly Sync<int> SegmentsY;
    public readonly Sync<float2> UVScale;

    private PhosGrid? _grid;
    private float2 _size;
    private int _segX;
    private int _segY;
    private float2 _uvScale;

    public GridMesh()
    {
        Size = new Sync<float2>(this, new float2(1f, 1f));
        SegmentsX = new Sync<int>(this, 12);
        SegmentsY = new Sync<int>(this, 12);
        UVScale = new Sync<float2>(this, new float2(1f, 1f));
    }

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(Size);
        SubscribeToChanges(SegmentsX);
        SubscribeToChanges(SegmentsY);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _size = Size.Value;
        _segX = System.Math.Clamp(SegmentsX.Value, 1, 128);
        _segY = System.Math.Clamp(SegmentsY.Value, 1, 128);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _grid != null && (_grid.SegmentsX != _segX || _grid.SegmentsY != _segY);
        uploadHint[MeshUploadHint.Flag.Geometry] = _grid == null || rebuild;

        if (_grid == null || rebuild)
        {
            if (_grid != null)
                mesh.Clear();
            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _grid = new PhosGrid(submesh, _segX, _segY);
        }

        _grid.Size = _size;
        _grid.UVScale = _uvScale;
        _grid.Update();
    }

    protected override void ClearMeshData()
    {
        _grid = null;
    }
}
