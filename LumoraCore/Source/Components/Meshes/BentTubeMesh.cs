// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// The run starts at the origin heading along +Y and curves toward +X, staying in the local XY plane,
// so a bend of 90 degrees ends pointing along +X. Arc length is BendRadius * Angle, so either field at
// zero gives a tube with no length.
[ComponentCategory("Assets/Procedural Meshes")]
public class BentTubeMesh : ProceduralMesh
{
    public readonly Sync<float> Radius;

    // measured to the tube centreline
    public readonly Sync<float> BendRadius;

    // degrees
    [Range(-360f, 360f, "0.0")]
    public readonly Sync<float> Angle;

    public readonly Sync<int> Sides;

    public readonly Sync<int> Segments;

    public readonly Sync<bool> Caps;

    public readonly Sync<float2> UVScale;

    private PhosTube? _tube;
    private float _radius;
    private float _bendRadius;
    private float _angle;
    private int _sides;
    private int _segments;
    private bool _caps;
    private float2 _uvScale;

    public BentTubeMesh()
    {
        Radius = new Sync<float>(this, 0.05f);
        BendRadius = new Sync<float>(this, 0.5f);
        Angle = new Sync<float>(this, 90f);
        Sides = new Sync<int>(this, 12);
        Segments = new Sync<int>(this, 16);
        Caps = new Sync<bool>(this, true);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Radius);
        SubscribeToChanges(BendRadius);
        SubscribeToChanges(Angle);
        SubscribeToChanges(Sides);
        SubscribeToChanges(Segments);
        SubscribeToChanges(Caps);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _radius = Radius.Value;
        _bendRadius = System.Math.Max(BendRadius.Value, 0f);
        _angle = Angle.Value;
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

        // Circle centred on +X at BendRadius, walked from the origin. At angle 0 the point is the
        // origin and the tangent is +Y, so a bend of 0 leaves the tube pointing the way a straight one
        // would rather than jumping somewhere else.
        float total = _angle * (System.MathF.PI / 180f);
        for (int i = 0; i <= _segments; i++)
        {
            float a = total * ((float)i / _segments);
            _tube.Path[i] = new float3(
                _bendRadius * (1f - System.MathF.Cos(a)),
                _bendRadius * System.MathF.Sin(a),
                0f);
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
