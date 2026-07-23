// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components;

// Collider shaped as the convex hull of a mesh or a point list. Unlike the mesh collider's convex
// mode, which hands the whole vertex buffer to the physics backend and lets it work the hull out, the
// hull is solved here and only the hull points cross to the hook. That keeps the cook small on dense
// imports and, more importantly, keeps the result identical on every peer: the shape a remote client
// collides against comes out of the same solver with the same input, not out of whatever the local
// backend decided. -xlinka
//
// The hull is cached and only re-solved when the source reference, the point list or the tolerance
// changes. Geometry edited in place behind a reference (a procedural mesh regenerating) raises no
// event here, so that case has the Rebuild Hull action.
[ComponentCategory("Physics/Colliders")]
public class ConvexHullCollider : Collider
{
    // optional if Points is filled instead
    public readonly SyncRef<Component> Mesh;

    // local space; used on their own or on top of Mesh
    public readonly SyncFieldList<float3> Points;

    // 0 falls back to a tolerance scaled to the cloud's own size
    public readonly Sync<float> MinPointDistance;

    // bumped whenever the cached hull stops being valid; the hook keys its bake on this
    public int HullVersion { get; private set; }

    private readonly List<float3> _gathered = new();
    private readonly List<float3> _hull = new();
    private readonly List<int> _hullIndices = new();
    private int _cachedVersion = -1;
    private ConvexHullSolver.Result _cachedResult;
    private int _meshReadyRetries;

    public ConvexHullCollider()
    {
        Mesh = new SyncRef<Component>(this);
        Points = new SyncFieldList<float3>(this);
        MinPointDistance = new Sync<float>(this, 0f);
    }

    public override void OnAwake()
    {
        base.OnAwake();

        Mesh.OnChanged += _ =>
        {
            _meshReadyRetries = 0;
            MarkHullDirty();
            ArmMeshReadyRetry();
        };
        Points.OnChanged += _ => MarkHullDirty();
        MinPointDistance.OnChanged += _ => MarkHullDirty();
    }

    public override void OnStart()
    {
        base.OnStart();
        ArmMeshReadyRetry();
    }

    // for geometry that changed behind the reference without the reference itself changing, which
    // raises no event on this side
    [SyncMethod]
    public void RebuildHull()
    {
        MarkHullDirty();
    }

    private void MarkHullDirty()
    {
        HullVersion++;
        RunApplyChanges();
    }

    // Asset-backed meshes decode off-thread and nothing re-drives the hook when the data lands, so the
    // shape would silently stay missing. Poll a few frames apart until the geometry exists, then push
    // one rebuild. Same wait the mesh collider uses.
    private void ArmMeshReadyRetry()
    {
        if (IsDestroyed || World == null)
            return;

        Component? source = Mesh.Target;
        if (source == null)
            return;

        if (ConvexHullPointSource.IsReady(source))
        {
            MarkHullDirty();
            return;
        }

        if (_meshReadyRetries++ > 600)
            return;

        World.RunInUpdates(10, ArmMeshReadyRetry);
    }

    // local space; solved on first request after a change and cached - the hook calls this every time
    // it rebuilds a shape, so it must not re-solve on every call
    public IReadOnlyList<float3> GetHullPoints()
    {
        if (_cachedVersion != HullVersion)
        {
            ConvexHullPointSource.Gather(Mesh.Target, Points, _gathered);
            _cachedResult = ConvexHullSolver.Solve(
                _gathered,
                System.Math.Max(MinPointDistance.Value, 0f),
                _hull,
                _hullIndices);
            _cachedVersion = HullVersion;
        }

        return _hull;
    }

    // read after GetHullPoints
    public ConvexHullSolver.Result LastResult => _cachedResult;

    public override BoundingBox GetLocalBounds()
    {
        var hull = GetHullPoints();
        if (hull.Count == 0)
        {
            // Nothing solved yet (or nothing solvable). Report a zero-extent box at the offset rather
            // than inventing a size.
            return new BoundingBox(Offset.Value, Offset.Value);
        }

        float3 min = hull[0];
        float3 max = hull[0];
        for (int i = 1; i < hull.Count; i++)
        {
            float3 p = hull[i];
            min = new float3(System.Math.Min(min.x, p.x), System.Math.Min(min.y, p.y), System.Math.Min(min.z, p.z));
            max = new float3(System.Math.Max(max.x, p.x), System.Math.Max(max.y, p.y), System.Math.Max(max.z, p.z));
        }

        return new BoundingBox(min + Offset.Value, max + Offset.Value);
    }
}
