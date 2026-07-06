// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using Lumora.Godot.Helpers;

namespace Lumora.Godot.Hooks;

/// <summary>
/// World hook for Godot - creates and manages world root node, and answers the world's physics
/// collision queries against the Godot/Jolt physics space.
/// </summary>
public class WorldHook : IWorldHook, IPhysicsQueryHook
{
    public World Owner { get; private set; } = null!;

    public Node3D WorldRoot { get; private set; } = null!;

    private readonly Dictionary<Node3D, int> _layerSnapshot = new();

    // Cap the skip-and-retry loop that scopes shared-space queries to this world.
    private const int MaxQueryIterations = 16;

    // Per-world collision layer bit: every physics body in this world carries it, and this world's
    // queries mask to it, so cross-world hits become impossible at the physics level instead of
    // being filtered by the retry loop (which stays as a fallback for legacy bit-0 bodies and
    // caller excludes). 31 bits rotate; bit 0 is the legacy default shared by pre-bit bodies.
    private static int _nextWorldBitIndex;
    public uint CollisionBit { get; private set; } = 1u;

    // The bit for a body that should collide/query within the given world (1 = legacy default).
    public static uint GetCollisionBitFor(World? world)
        => (world?.Hook as WorldHook)?.CollisionBit ?? 1u;

    public static WorldHook Constructor()
    {
        return new WorldHook();
    }

    public void Initialize(World owner)
    {
        Owner = owner;
        CollisionBit = 1u << (1 + System.Threading.Interlocked.Increment(ref _nextWorldBitIndex) % 31);

        // Get WorldManager hook to parent under
        var worldManagerHook = Owner.WorldManager.Hook as WorldManagerHook;
        // Create world root node
        WorldRoot = new Node3D();
        WorldRoot.Name = $"World_{Owner.WorldName.Value}";
        WorldRoot.Visible = false; // Start inactive

        // Parent under WorldManager root
        if (worldManagerHook?.Root != null)
        {
            worldManagerHook.Root.AddChild(WorldRoot);
        }
        else
        {
            GD.Print($"WorldHook.Initialize: WARNING - Could not parent WorldRoot (WorldManagerHook or Root is null)");
        }

        // Reset transform
        WorldRoot.Position = Vector3.Zero;
        WorldRoot.Rotation = Vector3.Zero;
        WorldRoot.Scale = Vector3.One;

        // Store reference in World for hooks to access
        Owner.GodotSceneRoot = WorldRoot;

        // Reparent any existing slot Node3Ds that were created before world root existed
        if (Owner.IsAuthority || Owner.State == World.WorldState.Running)
        {
            ReparentExistingSlots(Owner.RootSlot);
        }

        // Apply the world's current focus state (important for worlds that set focus before hook was created)
        ChangeFocus(Owner.Focus);
    }

    /// <summary>
    /// Recursively reparent existing slot Node3Ds to the world root.
    /// Called after WorldRoot is created to fix slots that were orphaned.
    /// </summary>
    private void ReparentExistingSlots(Lumora.Core.Slot slot)
    {
        if (slot == null) return;

        // If this slot has a hook with a generated Node3D, reparent it
        if (slot.Hook is SlotHook slotHook && slotHook.GeneratedNode3D != null)
        {
            Node3D node3D = slotHook.GeneratedNode3D;
            // For root slot, add directly to WorldRoot. Don't treat pending-parent slots as roots.
            bool isRootSlot = slot.IsRootSlot;
            bool isExplicitOrphan = slot.Parent == null && !slot.HasPendingParent && !slot.IsParentUnknown;
            if (isRootSlot || isExplicitOrphan)
            {
                // If node is orphaned (no parent), add it. Otherwise reparent it.
                if (node3D.GetParent() == null)
                {
                    WorldRoot.AddChild(node3D);
                    GD.Print($"WorldHook: Added orphaned root slot '{slot.SlotName.Value}' to WorldRoot");
                }
                else
                {
                    node3D.Reparent(WorldRoot, false);
                    GD.Print($"WorldHook: Reparented root slot '{slot.SlotName.Value}' to WorldRoot");
                }
            }
            else
            {
                // For child slots, ensure parent has its Node3D first
                if (slot.Parent?.Hook is SlotHook parentHook)
                {
                    Node3D parentNode3D = parentHook.GeneratedNode3D;
                    if (parentNode3D == null)
                    {
                        // Parent doesn't have a Node3D yet, create it
                        parentNode3D = parentHook.RequestNode3D();
                    }

                    // If node is orphaned (no parent), add it. Otherwise reparent it.
                    if (node3D.GetParent() == null)
                    {
                        parentNode3D.AddChild(node3D);
                        GD.Print($"WorldHook: Added orphaned slot '{slot.SlotName.Value}' to parent '{slot.Parent.SlotName.Value}'");
                    }
                    else
                    {
                        node3D.Reparent(parentNode3D, false);
                        GD.Print($"WorldHook: Reparented slot '{slot.SlotName.Value}' to parent '{slot.Parent.SlotName.Value}'");
                    }
                }
            }
        }

        // Recursively process children
        foreach (var child in slot.Children)
        {
            ReparentExistingSlots(child);
        }
    }

    public void ChangeFocus(World.WorldFocus focus)
    {
        switch (focus)
        {
            case World.WorldFocus.Focused:
            case World.WorldFocus.Overlay:
                WorldRoot.Visible = true;
                WorldRoot.ProcessMode = Node.ProcessModeEnum.Inherit;
                RenderHelper.RestoreHierarchyLayer(WorldRoot, _layerSnapshot);
                _layerSnapshot.Clear();
                break;

            case World.WorldFocus.PrivateOverlay:
                WorldRoot.Visible = true;
                WorldRoot.ProcessMode = Node.ProcessModeEnum.Inherit;
                RenderHelper.SetHierarchyLayer(WorldRoot, RenderHelper.PRIVATE_LAYER, _layerSnapshot);
                break;

            case World.WorldFocus.Background:
                WorldRoot.Visible = false;
                WorldRoot.ProcessMode = Node.ProcessModeEnum.Disabled;
                break;
        }
    }

    // IPhysicsQueryHook: route the world's collision queries to the Godot/Jolt physics space.

    public bool Raycast(in float3 origin, in float3 direction, float maxDistance, bool hitTriggers, IReadOnlyList<Lumora.Core.Slot>? exclude, out PhysicsRaycastHit hit)
    {
        hit = default;
        var space = GetSpace();
        if (space == null)
            return false;

        var from = new Vector3(origin.x, origin.y, origin.z);
        var dir = new Vector3(direction.x, direction.y, direction.z).Normalized();
        if (dir == Vector3.Zero)
            return false;

        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * maxDistance);
        query.CollideWithBodies = true;
        query.CollideWithAreas = hitTriggers; // grabbable / image colliders are Area3D sensors
        // This world's bit plus the legacy default bit (pre-bit bodies); the retry loop below
        // still world-checks whatever comes through.
        query.CollisionMask = CollisionBit | 1u;
        var excludeRids = new global::Godot.Collections.Array<Rid>();
        query.Exclude = excludeRids;

        // Scope to this world: all worlds currently share one Godot physics space, so skip + retry
        // past colliders that belong to a different world (or a caller-excluded slot) until a valid
        // in-world hit (or nothing's left).
        for (int i = 0; i < MaxQueryIterations; i++)
        {
            var result = space.IntersectRay(query);
            if (result.Count == 0)
                return false;

            var slot = ResolveSlot(result["collider"].As<Node>());
            if (InThisWorld(slot) && !IsExcluded(slot, exclude))
            {
                FillHit(ref hit, (Vector3)result["position"], (Vector3)result["normal"], from, slot);
                return true;
            }
            excludeRids.Add(result["rid"].As<Rid>());
        }
        return false;
    }

    // True if slot is, or descends from, any caller-excluded slot (so a whole avatar can be skipped
    // by passing just its root). Walks ancestors; cheap for the small exclude lists callers pass.
    private static bool IsExcluded(Lumora.Core.Slot? slot, IReadOnlyList<Lumora.Core.Slot>? exclude)
    {
        if (slot == null || exclude == null || exclude.Count == 0)
            return false;
        for (var s = slot; s != null; s = s.Parent)
        {
            for (int i = 0; i < exclude.Count; i++)
                if (ReferenceEquals(s, exclude[i]))
                    return true;
        }
        return false;
    }

    public bool SphereCast(in float3 origin, in float3 direction, float radius, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit)
        => ShapeCast(new SphereShape3D { Radius = radius }, Basis.Identity, in origin, in direction, maxDistance, hitTriggers, out hit);

    public bool BoxCast(in float3 origin, in float3 size, in floatQ orientation, in float3 direction, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit)
        => ShapeCast(new BoxShape3D { Size = new Vector3(size.x, size.y, size.z) }, ToBasis(orientation), in origin, in direction, maxDistance, hitTriggers, out hit);

    public bool CapsuleCast(in float3 origin, float radius, float height, in floatQ orientation, in float3 direction, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit)
        => ShapeCast(new CapsuleShape3D { Radius = radius, Height = height }, ToBasis(orientation), in origin, in direction, maxDistance, hitTriggers, out hit);

    public int OverlapSphere(in float3 origin, float radius, bool hitTriggers, List<Lumora.Core.Slot> results)
        => ShapeOverlap(new SphereShape3D { Radius = radius }, new Transform3D(Basis.Identity, new Vector3(origin.x, origin.y, origin.z)), hitTriggers, results);

    public int OverlapBox(in float3 origin, in float3 size, in floatQ orientation, bool hitTriggers, List<Lumora.Core.Slot> results)
        => ShapeOverlap(new BoxShape3D { Size = new Vector3(size.x, size.y, size.z) }, new Transform3D(ToBasis(orientation), new Vector3(origin.x, origin.y, origin.z)), hitTriggers, results);

    // Shape sweep (sphere/box/capsule) with world scoping. CastMotion gives [safe, unsafe] motion
    // fractions; GetRestInfo at the contact recovers point/normal/collider.
    private bool ShapeCast(Shape3D shape, Basis basis, in float3 origin, in float3 direction, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit)
    {
        hit = default;
        var space = GetSpace();
        if (space == null)
            return false;

        var from = new Vector3(origin.x, origin.y, origin.z);
        var dir = new Vector3(direction.x, direction.y, direction.z).Normalized();
        if (dir == Vector3.Zero)
            return false;

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            CollideWithBodies = true,
            CollideWithAreas = hitTriggers,
            CollisionMask = CollisionBit | 1u,
        };
        var exclude = new global::Godot.Collections.Array<Rid>();
        query.Exclude = exclude;

        for (int i = 0; i < MaxQueryIterations; i++)
        {
            query.Transform = new Transform3D(basis, from);
            query.Motion = dir * maxDistance;
            var motion = space.CastMotion(query);
            if (motion == null || motion.Length < 2 || motion[1] >= 1f)
                return false;

            float distance = motion[1] * maxDistance;
            var contact = from + dir * distance;
            query.Transform = new Transform3D(basis, contact);
            query.Motion = Vector3.Zero;

            var info = space.GetRestInfo(query);
            if (info.Count == 0)
                return false; // contact but no rest info to identify/scope the collider

            var slot = ResolveSlotById(info["collider_id"].AsUInt64());
            if (InThisWorld(slot))
            {
                FillHit(ref hit, (Vector3)info["point"], (Vector3)info["normal"], from, slot);
                hit.Distance = distance; // sweep travel to contact, not point distance
                return true;
            }
            exclude.Add(info["rid"].As<Rid>());
        }
        return false;
    }

    private int ShapeOverlap(Shape3D shape, Transform3D transform, bool hitTriggers, List<Lumora.Core.Slot> results)
    {
        var space = GetSpace();
        if (space == null)
            return 0;

        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = transform,
            CollideWithBodies = true,
            CollideWithAreas = hitTriggers,
            CollisionMask = CollisionBit | 1u,
        };

        foreach (var dict in space.IntersectShape(query, 32))
        {
            var slot = ResolveSlot(dict["collider"].As<Node>());
            if (InThisWorld(slot) && !results.Contains(slot!))
                results.Add(slot!);
        }
        return results.Count;
    }

    // Reused across the hundreds of per-particle soft-body resolves each frame - allocating a shape +
    // query params per particle would churn the GC hard. -xlinka
    private SphereShape3D? _resolveShape;
    private PhysicsShapeQueryParameters3D? _resolveQuery;
    private global::Godot.Collections.Array<Rid>? _resolveExclude;

    public bool ResolveSphere(in float3 center, float radius, IReadOnlyList<Lumora.Core.Slot>? exclude, out float3 correctedCenter, out float3 normal)
    {
        correctedCenter = center;
        normal = float3.Up;
        var space = GetSpace();
        if (space == null || radius <= 0f)
            return false;

        _resolveShape ??= new SphereShape3D();
        _resolveShape.Radius = radius;
        _resolveExclude ??= new global::Godot.Collections.Array<Rid>();
        _resolveExclude.Clear();
        var query = _resolveQuery ??= new PhysicsShapeQueryParameters3D();
        query.Shape = _resolveShape;
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;           // solids only - not grab/image sensors
        query.CollisionMask = CollisionBit | 1u;
        query.Exclude = _resolveExclude;

        var basis = Basis.Identity;
        var c = new Vector3(center.x, center.y, center.z);
        // GetRestInfo returns the single deepest contact; if that collider is foreign-world or excluded,
        // skip it and retry so an in-world contact underneath still resolves.
        for (int i = 0; i < MaxQueryIterations; i++)
        {
            query.Transform = new Transform3D(basis, c);
            var info = space.GetRestInfo(query);
            if (info.Count == 0)
                return false;

            var slot = ResolveSlotById(info["collider_id"].AsUInt64());
            if (InThisWorld(slot) && !IsExcluded(slot, exclude))
            {
                var point = (Vector3)info["point"];
                var n = (Vector3)info["normal"];
                correctedCenter = new float3(point.X + n.X * radius, point.Y + n.Y * radius, point.Z + n.Z * radius);
                normal = new float3(n.X, n.Y, n.Z);
                return true;
            }
            _resolveExclude.Add(info["rid"].As<Rid>());
        }
        return false;
    }

    private PhysicsDirectSpaceState3D? GetSpace()
    {
        if (WorldRoot == null || !GodotObject.IsInstanceValid(WorldRoot) || !WorldRoot.IsInsideTree())
            return null;
        return WorldRoot.GetWorld3D()?.DirectSpaceState;
    }

    private void FillHit(ref PhysicsRaycastHit hit, Vector3 point, Vector3 normal, Vector3 from, Lumora.Core.Slot? slot)
    {
        hit.Point = new float3(point.X, point.Y, point.Z);
        hit.Normal = new float3(normal.X, normal.Y, normal.Z);
        hit.Distance = from.DistanceTo(point);
        hit.Slot = slot;
    }

    // All worlds share one Godot physics space, so a query keeps only hits in this world. An
    // unresolved collider (no slot / foreign node) counts as not ours.
    private bool InThisWorld(Lumora.Core.Slot? slot) => slot != null && ReferenceEquals(slot.World, Owner);

    private static Basis ToBasis(in floatQ orientation)
        => new Basis(new Quaternion(orientation.x, orientation.y, orientation.z, orientation.w));

    // GetRestInfo (unlike IntersectRay/IntersectShape) returns "collider_id" - an ObjectID - not a
    // "collider" node. Resolve the node from the id, then recover the slot the normal way. -xlinka
    private Lumora.Core.Slot? ResolveSlotById(ulong instanceId)
    {
        if (instanceId == 0)
            return null;
        return ResolveSlot(GodotObject.InstanceFromId(instanceId) as Node);
    }

    // Physics bodies are tagged with their owning slot's RefID (LumoraSlotRef meta) by the collider
    // hooks; walk up from the hit node to recover the slot.
    private Lumora.Core.Slot? ResolveSlot(Node? collider)
    {
        var node = collider;
        while (node != null && GodotObject.IsInstanceValid(node))
        {
            if (node.HasMeta("LumoraSlotRef") &&
                ulong.TryParse(node.GetMeta("LumoraSlotRef").AsString(), out var raw))
            {
                return Owner?.ReferenceController?.GetObjectOrNull(new RefID(raw)) as Lumora.Core.Slot;
            }
            node = node.GetParent();
        }
        return null;
    }

    public void Destroy()
    {
        if (WorldRoot != null && GodotObject.IsInstanceValid(WorldRoot))
        {
            WorldRoot.QueueFree();
        }

        WorldRoot = null!;
        Owner = null!;
    }
}

