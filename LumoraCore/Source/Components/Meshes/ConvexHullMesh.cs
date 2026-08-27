// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// Points come from a referenced mesh, from the hand-written list, or from both. Useful for
// shrink-wrapping an imported prop into something cheap enough to collide against, and for turning a
// scatter of markers into a solid.
//
// The hull is rebuilt from scratch on every change because its vertex count follows the input; there
// is no fixed topology to update in place. That makes this the one procedural mesh here you should
// not drive per frame off a moving source. -xlinka
[ComponentCategory("Assets/Procedural Meshes")]
public class ConvexHullMesh : ProceduralMesh
{
    // optional if Points is filled instead
    public readonly SyncRef<Component> SourceMesh;

    // local space; used on their own or on top of SourceMesh
    public readonly SyncFieldList<float3> Points;

    public readonly Sync<bool> FlatShading;

    // raising it trades hull detail for speed on dense inputs; 0 falls back to a tolerance scaled to
    // the cloud's own size
    public readonly Sync<float> MinPointDistance;

    public readonly Sync<float2> UVScale;

    // empty means nothing was drawn
    public ConvexHullSolver.Result LastResult { get; private set; }

    private readonly List<float3> _gathered = new();
    private bool _flatShading;
    private float _minPointDistance;
    private float2 _uvScale;
    private int _sourceReadyRetries;

    public ConvexHullMesh()
    {
        SourceMesh = new SyncRef<Component>(this);
        Points = new SyncFieldList<float3>(this);
        FlatShading = new Sync<bool>(this, false);
        MinPointDistance = new Sync<float>(this, 0f);
        UVScale = new Sync<float2>(this, float2.One);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        SourceMesh.OnChanged += _ =>
        {
            _sourceReadyRetries = 0;
            RegenerateMesh();
            ArmSourceReadyRetry();
        };
        Points.OnChanged += _ => RegenerateMesh();
        SubscribeToChanges(FlatShading);
        SubscribeToChanges(MinPointDistance);
        SubscribeToChanges(UVScale);
    }

    public override void OnStart()
    {
        base.OnStart();
        ArmSourceReadyRetry();
    }

    // A referenced asset-backed mesh decodes off-thread and nothing re-drives this when it lands, so
    // the hull would sit empty forever. Poll a few frames apart until the geometry exists, then
    // regenerate once. Same shape as the mesh collider's wait.
    private void ArmSourceReadyRetry()
    {
        if (IsDestroyed || World == null)
            return;

        Component? source = SourceMesh.Target;
        if (source == null)
            return;

        if (ConvexHullPointSource.IsReady(source))
        {
            RegenerateMesh();
            return;
        }

        if (_sourceReadyRetries++ > 600)
            return;

        World.RunInUpdates(10, ArmSourceReadyRetry);
    }

    protected override void PrepareAssetUpdateData()
    {
        ConvexHullPointSource.Gather(SourceMesh.Target, Points, _gathered);
        _flatShading = FlatShading.Value;
        _minPointDistance = System.Math.Max(MinPointDistance.Value, 0f);
        _uvScale = UVScale.Value;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        uploadHint.SetAll();
        LastResult = PhosConvexHull.Build(mesh, _gathered, _minPointDistance, _flatShading, _uvScale);
    }

    protected override void ClearMeshData()
    {
        _gathered.Clear();
        LastResult = ConvexHullSolver.Result.Empty;
    }
}
