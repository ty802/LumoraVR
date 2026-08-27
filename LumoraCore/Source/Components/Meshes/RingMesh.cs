// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// Lies flat in the local XZ plane around Y, extruded to Height. Hard edges and a square
// cross-section, so it reads as a bezel or a dial rather than a torus.
[ComponentCategory("Assets/Procedural Meshes")]
public class RingMesh : ProceduralMesh
{
    public readonly Sync<float> InnerRadius;

    public readonly Sync<float> OuterRadius;

    // 0 gives a flat washer
    public readonly Sync<float> Height;

    public readonly Sync<int> Segments;

    public readonly Sync<float2> UVScale;

    private PhosRing? _ring;
    private float _innerRadius;
    private float _outerRadius;
    private float _height;
    private int _segments;
    private float2 _uvScale;

    public RingMesh()
    {
        InnerRadius = new Sync<float>(this, 0.4f);
        OuterRadius = new Sync<float>(this, 0.5f);
        Height = new Sync<float>(this, 0.05f);
        Segments = new Sync<int>(this, 48);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(InnerRadius);
        SubscribeToChanges(OuterRadius);
        SubscribeToChanges(Height);
        SubscribeToChanges(Segments);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _innerRadius = InnerRadius.Value;
        _outerRadius = OuterRadius.Value;
        _height = Height.Value;
        _segments = System.Math.Clamp(Segments.Value, 3, 512);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _ring != null && _ring.Segments != _segments;
        uploadHint[MeshUploadHint.Flag.Geometry] = _ring == null || rebuild;

        if (_ring == null || rebuild)
        {
            if (_ring != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _ring = new PhosRing(submesh, _segments);
        }

        _ring.InnerRadius = _innerRadius;
        _ring.OuterRadius = _outerRadius;
        _ring.Height = _height;
        _ring.UVScale = _uvScale;
        _ring.Update();
    }

    protected override void ClearMeshData()
    {
        _ring = null;
    }
}
