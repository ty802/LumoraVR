// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// Running along the local Y axis, centred on the origin. Length segments are exposed even though a
// straight tube does not need them: subdividing along the run is what lets a vertex shader, a soft
// body or a blend shape bend it afterwards.
[ComponentCategory("Assets/Procedural Meshes")]
public class TubeMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;

    public readonly Sync<float> Length;

    public readonly Sync<int> Sides;

    public readonly Sync<int> Segments;

    public readonly Sync<bool> Caps;

    public readonly Sync<float2> UVScale;

    private PhosTube? _tube;
    private float _radius;
    private float _length;
    private int _sides;
    private int _segments;
    private bool _caps;
    private float2 _uvScale;

    public TubeMesh()
    {
        Radius = new Sync<float>(this, 0.05f);
        Length = new Sync<float>(this, 1f);
        Sides = new Sync<int>(this, 12);
        Segments = new Sync<int>(this, 1);
        Caps = new Sync<bool>(this, true);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Radius);
        SubscribeToChanges(Length);
        SubscribeToChanges(Sides);
        SubscribeToChanges(Segments);
        SubscribeToChanges(Caps);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = Radius.Value;
        _length = Length.Value;
        _sides = System.Math.Clamp(Sides.Value, 3, 256);
        _segments = System.Math.Clamp(Segments.Value, 1, 512);
        _caps = Caps.Value;
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _tube != null
            && (_tube.Sides != _sides || _tube.Segments != _segments || _tube.Caps != _caps);
        uploadHint[MeshUploadHint.Flag.Geometry] = _tube == null || rebuild;

        if (_tube == null || rebuild)
        {
            if (_tube != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _tube = new PhosTube(submesh, _sides, _segments, _caps);
        }

        float half = _length * 0.5f;
        for (int i = 0; i <= _segments; i++)
        {
            float t = (float)i / _segments;
            _tube.Path[i] = new float3(0f, -half + _length * t, 0f);
        }

        _tube.SetRadius(_radius);
        _tube.UVScale = _uvScale;
        _tube.Update();
    }

    protected override void ClearMeshData()
    {
        _tube = null;
    }
}
