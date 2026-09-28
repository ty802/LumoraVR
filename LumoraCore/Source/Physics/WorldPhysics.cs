// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

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

    // RESTING CONTACT

    /// <summary>
    /// Push a sphere out of any world collider it penetrates (resting contact). Returns true and sets
    /// <paramref name="correctedCenter"/> + <paramref name="normal"/> on contact. Used by soft bodies to
    /// drape/rest on arbitrary geometry - handles a stationary overlap, which a movement raycast cannot.
    /// </summary>
    public bool ResolveSphere(in float3 center, float radius, IReadOnlyList<Slot>? exclude, out float3 correctedCenter, out float3 normal)
    {
        var hook = QueryHook;
        if (hook == null)
        {
            correctedCenter = center;
            normal = new float3(0f, 1f, 0f);
            return false;
        }
        return hook.ResolveSphere(in center, radius, exclude, out correctedCenter, out normal);
    }

    // CONTACTS
    // What a body touched and let go of. The physics hook reports the touches of the bodies this machine
    // simulates, from the body's side. Each is told to whatever watches that body and, turned round, to
    // whatever watches the thing it touched, so a wall hears the ball that hit it. World thread only.
    // -xlinka

    private sealed class ContactWatch
    {
        public Action<PhysicsContact>? Began;
        public Action<PhysicsContact>? Ended;
    }

    private readonly Dictionary<Slot, List<ContactWatch>> _contactWatches = new();

    // Returns what stops the watch.
    public Action WatchContacts(Slot slot, Action<PhysicsContact>? began, Action<PhysicsContact>? ended)
    {
        var watch = new ContactWatch { Began = began, Ended = ended };
        if (!_contactWatches.TryGetValue(slot, out var watches))
        {
            watches = new List<ContactWatch>(1);
            _contactWatches[slot] = watches;
        }
        watches.Add(watch);
        return () =>
        {
            if (_contactWatches.TryGetValue(slot, out var list) && list.Remove(watch) && list.Count == 0)
                _contactWatches.Remove(slot);
        };
    }

    public bool HasContactWatchers(Slot? slot) => slot != null && _contactWatches.ContainsKey(slot);

    public void ReportContactBegan(Slot self, Slot? other, in float3 point, in float3 normal, float impulse)
    {
        Tell(self, new PhysicsContact(self, other, point, normal, impulse), began: true);
        if (other != null && !ReferenceEquals(other, self))
            Tell(other, new PhysicsContact(other, self, point, -normal, impulse), began: true);
    }

    public void ReportContactEnded(Slot self, Slot? other)
    {
        Tell(self, new PhysicsContact(self, other, float3.Zero, float3.Zero, 0f), began: false);
        if (other != null && !ReferenceEquals(other, self))
            Tell(other, new PhysicsContact(other, self, float3.Zero, float3.Zero, 0f), began: false);
    }

    private void Tell(Slot slot, PhysicsContact contact, bool began)
    {
        if (!_contactWatches.TryGetValue(slot, out var watches))
            return;
        foreach (var watch in watches.ToArray())
        {
            try
            {
                (began ? watch.Began : watch.Ended)?.Invoke(contact);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"WorldPhysics: a contact watcher threw: {ex.Message}");
            }
        }
    }

    // PEOPLE
    // Whose body reaches into a sphere or a box. A body is the upright capsule a person stands in: at
    // their head's spot over their feet, from the feet to the head, as wide as the character collider on
    // their user root. Worked out from where heads and feet are, which every machine knows, so it finds
    // everyone, including the people this machine has no physics body for. -xlinka

    public const float DefaultBodyRadius = 0.3f;

    private readonly List<User> _people = new();

    public static bool TryGetBody(User? user, out float3 bottom, out float3 top, out float radius)
    {
        bottom = float3.Zero;
        top = float3.Zero;
        radius = 0f;
        // User.Root is filled in on the host and for the local user only; the reference is there everywhere.
        var root = user?.Root ?? user?.UserRootRef.Target;
        var slot = root?.Slot;
        if (user == null || user.IsDestroyed || root == null || root.IsDestroyed || slot == null || slot.IsDestroyed)
            return false;

        var scale = slot.GlobalScale;
        float size = System.Math.Max(System.Math.Abs(scale.x), System.Math.Abs(scale.z));
        radius = DefaultBodyRadius * size;
        foreach (var capsule in slot.GetComponents<CapsuleCollider>())
        {
            if (capsule.Type.Value == ColliderType.CharacterController && capsule.Radius.Value > 0f)
            {
                radius = capsule.Radius.Value * size;
                break;
            }
        }
        var head = root.HeadPosition;
        float feet = slot.GlobalPosition.y;
        bottom = new float3(head.x, feet + radius, head.z);
        top = new float3(head.x, System.Math.Max(feet + radius, head.y), head.z);
        return float.IsFinite(bottom.x) && float.IsFinite(bottom.y) && float.IsFinite(bottom.z) && float.IsFinite(top.y) && float.IsFinite(radius);
    }

    public int OverlapPeople(in float3 center, float radius, List<User> results)
    {
        results.Clear();
        _world.GetAllUsers(_people);
        foreach (var user in _people)
        {
            if (TryGetBody(user, out var bottom, out var top, out float body)
                && SegmentPointDistance(bottom, top, center) <= radius + body)
                results.Add(user);
        }
        return results.Count;
    }

    // Size edge to edge, like OverlapBox.
    public int OverlapPeople(in float3 center, in float3 size, in floatQ orientation, List<User> results)
    {
        results.Clear();
        var inverse = orientation.Inverse;
        var half = size * 0.5f;
        _world.GetAllUsers(_people);
        foreach (var user in _people)
        {
            if (TryGetBody(user, out var bottom, out var top, out float body)
                && SegmentBoxDistance(inverse * (bottom - center), inverse * (top - center), half) <= body)
                results.Add(user);
        }
        return results.Count;
    }

    private static float SegmentPointDistance(float3 a, float3 b, float3 point)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared;
        float t = lengthSquared > 1e-12f ? System.Math.Clamp(float3.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return (a + ab * t - point).Length;
    }

    // The distance from a segment to a box centred on the origin. The distance to a box is convex along
    // the segment, so a ternary search finds its least.
    private static float SegmentBoxDistance(float3 a, float3 b, float3 half)
    {
        float low = 0f, high = 1f;
        for (int i = 0; i < 40; i++)
        {
            float m1 = low + (high - low) / 3f;
            float m2 = high - (high - low) / 3f;
            if (PointBoxDistance(a + (b - a) * m1, half) <= PointBoxDistance(a + (b - a) * m2, half))
                high = m2;
            else
                low = m1;
        }
        return PointBoxDistance(a + (b - a) * ((low + high) * 0.5f), half);
    }

    private static float PointBoxDistance(float3 point, float3 half)
    {
        float dx = System.Math.Max(System.Math.Abs(point.x) - half.x, 0f);
        float dy = System.Math.Max(System.Math.Abs(point.y) - half.y, 0f);
        float dz = System.Math.Max(System.Math.Abs(point.z) - half.z, 0f);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
