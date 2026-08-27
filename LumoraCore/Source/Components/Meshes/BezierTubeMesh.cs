// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// The four control points are plain synced fields rather than a list, so each one can be driven
// independently: pin the ends to two slots, drive the handles off their forward vectors, and the tube
// tracks a moving cable or hose on its own.
//
// Samples are taken at even parameter steps, not even arc length. A bezier with lopsided handles
// bunches its samples where the curve is slack, which shows up as uneven texture stretch, not as a
// kink. Raise Samples if a particular curve shows it. -xlinka
[ComponentCategory("Assets/Procedural Meshes")]
public class BezierTubeMesh : ProceduralMesh
{
    // curve start
    public readonly Sync<float3> Point0;

    // handle leaving the start
    public readonly Sync<float3> Point1;

    // handle arriving at the end
    public readonly Sync<float3> Point2;

    // curve end
    public readonly Sync<float3> Point3;

    public readonly Sync<float> Radius;

    public readonly Sync<int> Sides;

    public readonly Sync<int> Samples;

    public readonly Sync<bool> Caps;

    public readonly Sync<float2> UVScale;

    private PhosTube? _tube;
    private float3 _p0;
    private float3 _p1;
    private float3 _p2;
    private float3 _p3;
    private float _radius;
    private int _sides;
    private int _samples;
    private bool _caps;
    private float2 _uvScale;

    public BezierTubeMesh()
    {
        Point0 = new Sync<float3>(this, new float3(0f, 0f, 0f));
        Point1 = new Sync<float3>(this, new float3(0f, 0.33f, 0f));
        Point2 = new Sync<float3>(this, new float3(0.5f, 0.67f, 0f));
        Point3 = new Sync<float3>(this, new float3(0.5f, 1f, 0f));
        Radius = new Sync<float>(this, 0.05f);
        Sides = new Sync<int>(this, 12);
        Samples = new Sync<int>(this, 24);
        Caps = new Sync<bool>(this, true);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SubscribeToChanges(Point0);
        SubscribeToChanges(Point1);
        SubscribeToChanges(Point2);
        SubscribeToChanges(Point3);
        SubscribeToChanges(Radius);
        SubscribeToChanges(Sides);
        SubscribeToChanges(Samples);
        SubscribeToChanges(Caps);
        SubscribeToChanges(UVScale);
    }

    protected override void PrepareAssetUpdateData()
    {
        _p0 = Point0.Value;
        _p1 = Point1.Value;
        _p2 = Point2.Value;
        _p3 = Point3.Value;
        _radius = Radius.Value;
        _sides = System.Math.Clamp(Sides.Value, 3, 256);
        _samples = System.Math.Clamp(Samples.Value, 1, 512);
        _caps = Caps.Value;
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        bool rebuild = _tube != null
            && (_tube.Sides != _sides || _tube.Segments != _samples || _tube.Caps != _caps);
        uploadHint[MeshUploadHint.Flag.Geometry] = _tube == null || rebuild;

        if (_tube == null || rebuild)
        {
            if (_tube != null)
                mesh.Clear();

            var submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(submesh);
            _tube = new PhosTube(submesh, _sides, _samples, _caps);
        }

        for (int i = 0; i <= _samples; i++)
        {
            _tube.Path[i] = Evaluate((float)i / _samples);
        }

        _tube.SetRadius(_radius);
        _tube.UVScale = _uvScale;
        _tube.Update();
    }

    private float3 Evaluate(float t)
    {
        float u = 1f - t;
        float uu = u * u;
        float tt = t * t;
        return _p0 * (uu * u)
            + _p1 * (3f * uu * t)
            + _p2 * (3f * u * tt)
            + _p3 * (tt * t);
    }

    protected override void ClearMeshData()
    {
        _tube = null;
    }
}
