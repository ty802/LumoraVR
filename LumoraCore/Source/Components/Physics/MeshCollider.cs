// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Mesh-shaped collider. Uses the referenced mesh for collision geometry.
/// Maps to Godot's ConcavePolygonShape3D (or ConvexPolygonShape3D if Convex is true).
/// </summary>
[ComponentCategory("Physics/Colliders")]
public class MeshCollider : Collider
{
    /// <summary>
    /// Reference to the mesh component to use for collision.
    /// </summary>
    public readonly SyncRef<Component> Mesh;

    /// <summary>
    /// If true, uses convex hull instead of concave mesh (better performance, less accurate).
    /// </summary>
    public readonly Sync<bool> Convex;

    public MeshCollider()
    {
        Mesh = new SyncRef<Component>(this);
        Convex = new Sync<bool>(this, false);
    }

    private int _meshReadyRetries;

    public override void OnAwake()
    {
        base.OnAwake();
        Mesh.OnChanged += _ =>
        {
            _meshReadyRetries = 0;
            RunApplyChanges();
            ArmMeshReadyRetry();
        };
        Convex.OnChanged += _ => RunApplyChanges();
    }

    public override void OnStart()
    {
        base.OnStart();
        ArmMeshReadyRetry();
    }

    // The provider's mesh data decodes async and nothing re-drives the hook when it lands (Mesh is a
    // plain SyncRef, not an asset ref), so the shape would silently stay missing. Poll a few frames
    // apart until the data exists, then push one ApplyChanges. -xlinka
    private void ArmMeshReadyRetry()
    {
        if (IsDestroyed || World == null)
            return;

        bool ready = Mesh.Target switch
        {
            ProceduralMesh procedural => procedural.PhosMesh != null,
            MeshProvider provider => provider.Asset?.MeshData != null,
            _ => false
        };

        if (ready)
        {
            RunApplyChanges();
            return;
        }

        if (Mesh.Target == null || _meshReadyRetries++ > 600)
            return;

        World.RunInUpdates(10, ArmMeshReadyRetry);
    }

    public override BoundingBox GetLocalBounds()
    {
        // Bounds come from the referenced mesh's own geometry, offset by this collider's Offset.
        // Until the mesh resolves (joiner, async decode) there is nothing to bound, so report a
        // zero-extent box at Offset rather than a fabricated size.
        if (TryGetMeshBounds(out BoundingBox local))
        {
            return new BoundingBox(local.Min + Offset.Value, local.Max + Offset.Value);
        }

        return new BoundingBox(Offset.Value, Offset.Value);
    }

    private bool TryGetMeshBounds(out BoundingBox bounds)
    {
        switch (Mesh.Target)
        {
            case ProceduralMesh procedural:
                bounds = procedural.GetBoundingBox();
                return true;
            case MeshProvider provider when provider.Asset != null:
                bounds = provider.Asset.Bounds;
                return true;
        }

        bounds = default;
        return false;
    }
}

