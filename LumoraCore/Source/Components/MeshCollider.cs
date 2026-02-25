using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using AquaLogger = Lumora.Core.Logging.Logger;

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

    public override void OnAwake()
    {
        base.OnAwake();
        Mesh.OnChanged += _ => RunApplyChanges();
        Convex.OnChanged += _ => RunApplyChanges();
    }

    public override object CreateGodotShape()
    {
        // Created by PhysicsColliderHook
        return null;
    }

    public override object GetLocalBounds()
    {
        return new { Min = float3.Zero, Max = float3.Zero };
    }
}
