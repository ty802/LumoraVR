// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Physics;

/// <summary>
/// Platform contract for collision queries against a world's physics space. Implemented by the
/// per-world hook (which owns the platform physics scene, e.g. Godot/Jolt) and consumed through
/// <see cref="WorldPhysics"/>. The engine doesn't simulate - it asks the platform.
///
/// A focused subset of the full sweep/overlap surface: a ray cast, a sphere sweep, and a sphere
/// overlap, each with a <c>hitTriggers</c> toggle (whether sensor/trigger colliders count). Box /
/// capsule sweeps and a collider predicate filter can be added when a component needs them.
/// </summary>
public interface IPhysicsQueryHook
{
    /// <summary>
    /// Cast a ray from <paramref name="origin"/> along <paramref name="direction"/> up to
    /// <paramref name="maxDistance"/> meters. Returns true and fills <paramref name="hit"/> on contact.
    /// </summary>
    /// <param name="exclude">Slots (and their descendants) whose colliders the ray skips - e.g. the
    /// caster's own avatar so a ground probe doesn't hit itself. Null/empty skips nothing.</param>
    bool Raycast(in float3 origin, in float3 direction, float maxDistance, bool hitTriggers, IReadOnlyList<Slot>? exclude, out PhysicsRaycastHit hit);

    /// <summary>
    /// Sweep a sphere of <paramref name="radius"/> from <paramref name="origin"/> along
    /// <paramref name="direction"/> up to <paramref name="maxDistance"/>. Returns true and fills
    /// <paramref name="hit"/> at the first contact.
    /// </summary>
    bool SphereCast(in float3 origin, in float3 direction, float radius, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit);

    /// <summary>Sweep an oriented box (<paramref name="size"/> = full extents) along a ray.</summary>
    bool BoxCast(in float3 origin, in float3 size, in floatQ orientation, in float3 direction, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit);

    /// <summary>Sweep an oriented capsule (<paramref name="height"/> = full height) along a ray.</summary>
    bool CapsuleCast(in float3 origin, float radius, float height, in floatQ orientation, in float3 direction, float maxDistance, bool hitTriggers, out PhysicsRaycastHit hit);

    /// <summary>
    /// Collect the slots whose colliders overlap a sphere at <paramref name="origin"/>. Returns the count.
    /// </summary>
    int OverlapSphere(in float3 origin, float radius, bool hitTriggers, List<Slot> results);

    /// <summary>Collect the slots whose colliders overlap an oriented box (<paramref name="size"/> = full extents).</summary>
    int OverlapBox(in float3 origin, in float3 size, in floatQ orientation, bool hitTriggers, List<Slot> results);

    /// <summary>
    /// Resting-contact push-out: if a sphere of <paramref name="radius"/> at <paramref name="center"/>
    /// penetrates any world collider, return true and set <paramref name="correctedCenter"/> to the
    /// nearest non-penetrating position (surface contact point + radius along the outward normal). Unlike
    /// a movement raycast this resolves a STATIONARY overlap, so soft bodies rest on / drape over any
    /// collider shape (box, sphere, capsule, mesh) without sinking through. Skips <paramref name="exclude"/>.
    /// </summary>
    bool ResolveSphere(in float3 center, float radius, IReadOnlyList<Slot>? exclude, out float3 correctedCenter, out float3 normal);
}
