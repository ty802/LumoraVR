// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Physics;

/// <summary>
/// Per-world, component-facing physics service: collision queries (routed to the platform physics
/// engine via <see cref="IPhysicsQueryHook"/>) plus world physics settings. This is the manager
/// components call (<c>World.Physics.Raycast(...)</c>); the actual simulation is the platform's
/// (Godot/Jolt), not the engine's. Queries no-op gracefully if no query hook is bound yet.
/// </summary>
public sealed class WorldPhysics
{
    private const float DefaultMaxDistance = 1000f;

    private readonly World _world;

    public WorldPhysics(World world) => _world = world;

    /// <summary>
    /// Configured world gravity (m/s²). The platform engine owns the actual simulation; this is the
    /// value the world advertises (e.g. for components that need it directly).
    /// </summary>
    public float3 Gravity { get; set; } = new float3(0f, -9.81f, 0f);

    private IPhysicsQueryHook? QueryHook => _world.Hook as IPhysicsQueryHook;

    // RAYCAST

    /// <summary>
    /// Cast a ray against this world's collision bodies. Returns false (and a default hit) if the
    /// platform query hook isn't available. Set <paramref name="hitTriggers"/> to also hit sensors.
    /// </summary>
    public bool Raycast(in float3 origin, in float3 direction, float maxDistance, out PhysicsRaycastHit hit, bool hitTriggers = false)
        => RaycastInternal(in origin, in direction, maxDistance, hitTriggers, null, out hit);

    /// <summary>
    /// Ray cast that skips <paramref name="exclude"/> (slots and their descendants) - e.g. pass the
    /// caster's own avatar root so a ground probe doesn't hit itself.
    /// </summary>
    public bool Raycast(in float3 origin, in float3 direction, float maxDistance, IReadOnlyList<Slot>? exclude, out PhysicsRaycastHit hit, bool hitTriggers = false)
        => RaycastInternal(in origin, in direction, maxDistance, hitTriggers, exclude, out hit);

    /// <summary>Convenience overload: returns the hit, or null on miss / no query hook.</summary>
    public PhysicsRaycastHit? Raycast(float3 origin, float3 direction, float maxDistance = DefaultMaxDistance, bool hitTriggers = false)
        => RaycastInternal(in origin, in direction, maxDistance, hitTriggers, null, out var hit) ? hit : null;

    /// <summary>Convenience overload with an exclude list: returns the hit, or null on miss.</summary>
    public PhysicsRaycastHit? Raycast(float3 origin, float3 direction, IReadOnlyList<Slot>? exclude, float maxDistance = DefaultMaxDistance, bool hitTriggers = false)
        => RaycastInternal(in origin, in direction, maxDistance, hitTriggers, exclude, out var hit) ? hit : null;

    private bool RaycastInternal(in float3 origin, in float3 direction, float maxDistance, bool hitTriggers, IReadOnlyList<Slot>? exclude, out PhysicsRaycastHit hit)
    {
        var hook = QueryHook;
        if (hook == null)
        {
            hit = default;
            return false;
        }
        return hook.Raycast(in origin, in direction, maxDistance, hitTriggers, exclude, out hit);
    }

    // SPHERE SWEEP

    /// <summary>Sweep a sphere along a ray; returns false (default hit) if no query hook.</summary>
    public bool SphereCast(in float3 origin, in float3 direction, float radius, float maxDistance, out PhysicsRaycastHit hit, bool hitTriggers = false)
    {
        var hook = QueryHook;
        if (hook == null)
        {
            hit = default;
            return false;
        }
        return hook.SphereCast(in origin, in direction, radius, maxDistance, hitTriggers, out hit);
    }

    /// <summary>Convenience overload: returns the sweep hit, or null on miss / no query hook.</summary>
    public PhysicsRaycastHit? SphereCast(float3 origin, float3 direction, float radius, float maxDistance = DefaultMaxDistance, bool hitTriggers = false)
        => SphereCast(in origin, in direction, radius, maxDistance, out var hit, hitTriggers) ? hit : null;

    /// <summary>Sweep an oriented box (<paramref name="size"/> = full extents) along a ray.</summary>
    public bool BoxCast(in float3 origin, in float3 size, in floatQ orientation, in float3 direction, float maxDistance, out PhysicsRaycastHit hit, bool hitTriggers = false)
    {
        var hook = QueryHook;
        if (hook == null)
        {
            hit = default;
            return false;
        }
        return hook.BoxCast(in origin, in size, in orientation, in direction, maxDistance, hitTriggers, out hit);
    }

    /// <summary>Sweep an oriented capsule (<paramref name="height"/> = full height) along a ray.</summary>
    public bool CapsuleCast(in float3 origin, float radius, float height, in floatQ orientation, in float3 direction, float maxDistance, out PhysicsRaycastHit hit, bool hitTriggers = false)
    {
        var hook = QueryHook;
        if (hook == null)
        {
            hit = default;
            return false;
        }
        return hook.CapsuleCast(in origin, radius, height, in orientation, in direction, maxDistance, hitTriggers, out hit);
    }

    // OVERLAP

    /// <summary>Fill <paramref name="results"/> with slots overlapping a sphere; returns the count (0 if no hook).</summary>
    public int OverlapSphere(float3 origin, float radius, List<Slot> results, bool hitTriggers = false)
    {
        results.Clear();
        return QueryHook?.OverlapSphere(in origin, radius, hitTriggers, results) ?? 0;
    }

    /// <summary>Fill <paramref name="results"/> with slots overlapping an oriented box (full extents); returns the count.</summary>
    public int OverlapBox(float3 origin, float3 size, floatQ orientation, List<Slot> results, bool hitTriggers = false)
    {
        results.Clear();
        return QueryHook?.OverlapBox(in origin, in size, in orientation, hitTriggers, results) ?? 0;
    }
}
