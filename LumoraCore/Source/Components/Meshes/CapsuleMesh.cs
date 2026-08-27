// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// a cylinder along the local Y axis with a hemisphere on each end; Height is the cylindrical section
// only, so the total extent is Height + 2 * Radius, matching the capsule collider
[ComponentCategory("Assets/Procedural Meshes")]
public class CapsuleMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;

    // excludes the caps
    public readonly Sync<float> Height;

    public readonly Sync<int> Segments;

    public readonly Sync<int> Rings;

    public readonly Sync<float2> UVScale;

    private PhosCapsule? _capsule;
    private float _radius;
    private float _height;
    private int _segments;
    private int _rings;
    private float2 _uvScale;

    public CapsuleMesh()
    {
        Radius = new Sync<float>(this, 0.25f);
        Height = new Sync<float>(this, 0.5f);
        Segments = new Sync<int>(this, 24);
        Rings = new Sync<int>(this, 8);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Radius);
        SubscribeToChanges(Height);
        SubscribeToChanges(Segments);
        SubscribeToChanges(Rings);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = Radius.Value;
        _height = Height.Value;
        _segments = System.Math.Clamp(Segments.Value, 3, 256);
        _rings = System.Math.Clamp(Rings.Value, 1, 128);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        // Segment and ring counts are baked into the topology; changing either rebuilds.
        bool rebuild = _capsule != null && (_capsule.Segments != _segments || _capsule.Rings != _rings);
        uploadHint[MeshUploadHint.Flag.Geometry] = _capsule == null || rebuild;

        if (_capsule == null || rebuild)
        {
            if (_capsule != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _capsule = new PhosCapsule(submesh, _segments, _rings);
        }

        _capsule.Radius = _radius;
        _capsule.Height = _height;
        _capsule.UVScale = _uvScale;
        _capsule.Update();
    }

    protected override void ClearMeshData()
    {
        _capsule = null;
    }
}
