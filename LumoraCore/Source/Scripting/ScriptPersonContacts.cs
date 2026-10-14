// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Who touches the colliders of a watched slot, for On Person Enter, Stay and Exit. A person is the upright
// capsule World.Physics finds people by; a collider counts while it is enabled and collides with anything at
// all, a box or a sphere by its shape and any other by its bounds, with a couple of centimetres to spare so
// a body standing against a solid wall touches it.
//
// No physics question is asked. Each person's capsule is worked out once a frame for every script of the
// world, and a watched slot no capsule comes near costs one box test per person. Scripts look twenty times
// a second each, spread over the gap between looks, so a world with hundreds of watched slots pays a share of
// them each frame. -xlinka
internal sealed class ScriptPersonContacts
{
    private const float Skin = 0.02f;

    private readonly ScriptHostRuntime _runtime;
    private readonly List<Watch> _watches = new();
    private double _next = double.NegativeInfinity;
    private readonly double _phase;

    private static int _started;

    private sealed class Watch
    {
        public Slot Slot = null!;
        public readonly List<ScriptEventEntry> Entries = new();
        public Dictionary<User, Slot?> Inside = new();
        public Dictionary<User, Slot?> Now = new();
    }

    private readonly record struct Body(User User, Slot Root, float3 Bottom, float3 Top, float Radius, float3 Min, float3 Max);

    private sealed class Bodies
    {
        public ulong Frame = ulong.MaxValue;
        public readonly List<Body> List = new();
        public readonly List<User> Users = new();
    }

    private static readonly ConditionalWeakTable<World, Bodies> _bodies = new();

    public ScriptPersonContacts(ScriptHostRuntime runtime)
    {
        _runtime = runtime;
        _phase = (Interlocked.Increment(ref _started) & 15) / 16.0 * ScriptEventSources.TriggerSeconds;
    }

    public void Add(Slot slot, ScriptEventEntry entry)
    {
        var watch = _watches.Find(w => ReferenceEquals(w.Slot, slot));
        if (watch == null)
        {
            watch = new Watch { Slot = slot };
            _watches.Add(watch);
        }
        watch.Entries.Add(entry);
    }

    public void Clear()
    {
        _watches.Clear();
        _next = double.NegativeInfinity;
    }

    public void Poll(double now)
    {
        if (_watches.Count == 0)
            return;
        if (double.IsNegativeInfinity(_next))
            _next = now + _phase;
        if (now < _next)
            return;
        _next = now + ScriptEventSources.TriggerSeconds;
        var world = _runtime.Host.World;
        if (world == null)
            return;
        var bodies = BodiesOf(world);
        foreach (var watch in _watches)
        {
            watch.Now.Clear();
            var slot = watch.Slot;
            if (!slot.IsDestroyed && slot.IsActive && bodies.Count > 0)
                Touching(slot, bodies, watch.Now);
            foreach (var (user, root) in watch.Now)
            {
                if (!watch.Inside.ContainsKey(user))
                    Raise(watch.Entries, new PersonEntered(user, root));
            }
            foreach (var (user, root) in watch.Inside)
            {
                if (!watch.Now.ContainsKey(user))
                    Raise(watch.Entries, new PersonExited(user, root is { IsDestroyed: false } ? root : null));
            }
            foreach (var (user, root) in watch.Now)
            {
                if (watch.Inside.ContainsKey(user))
                    Raise(watch.Entries, new PersonStaying(user, root));
            }
            (watch.Inside, watch.Now) = (watch.Now, watch.Inside);
        }
    }

    private void Raise<T>(List<ScriptEventEntry> entries, T payload) where T : struct
    {
        foreach (var entry in entries)
        {
            if (entry.PayloadType != typeof(T))
                continue;
            var node = entry.NodeId;
            _runtime.Enqueue(runner => runner.Raise(node, payload));
        }
    }

    // Everyone whose capsule reaches into a collider on the slot, a person whose own body holds the slot left out.
    private static void Touching(Slot slot, List<Body> bodies, Dictionary<User, Slot?> into)
    {
        var components = slot.Components;
        for (int c = 0; c < components.Count; c++)
        {
            if (components[c] is not Collider { IsDestroyed: false } collider || !collider.Enabled.Value
                || collider.Type.Value == ColliderType.NoCollision)
                continue;
            var scale = slot.GlobalScale;
            var size = new float3(MathF.Abs(scale.x), MathF.Abs(scale.y), MathF.Abs(scale.z));
            if (collider is SphereCollider sphere)
            {
                var middle = slot.LocalPointToGlobal(sphere.Offset.Value);
                float reach = sphere.Radius.Value * MathF.Max(size.x, MathF.Max(size.y, size.z)) + Skin;
                var low = middle - new float3(reach, reach, reach);
                var high = middle + new float3(reach, reach, reach);
                foreach (var body in bodies)
                {
                    if (Overlaps(body, low, high) && !Owns(body, slot)
                        && SegmentPointDistance(body.Bottom, body.Top, middle) <= reach + body.Radius)
                        into[body.User] = body.Root;
                }
                continue;
            }
            var bounds = collider.GetLocalBounds();
            var center = slot.LocalPointToGlobal((bounds.Min + bounds.Max) * 0.5f);
            var half = (bounds.Max - bounds.Min) * 0.5f;
            half = new float3(MathF.Abs(half.x) * size.x, MathF.Abs(half.y) * size.y, MathF.Abs(half.z) * size.z);
            var rotation = slot.GlobalRotation;
            var ax = rotation * new float3(1f, 0f, 0f);
            var ay = rotation * new float3(0f, 1f, 0f);
            var az = rotation * new float3(0f, 0f, 1f);
            var extent = new float3(
                MathF.Abs(ax.x) * half.x + MathF.Abs(ay.x) * half.y + MathF.Abs(az.x) * half.z + Skin,
                MathF.Abs(ax.y) * half.x + MathF.Abs(ay.y) * half.y + MathF.Abs(az.y) * half.z + Skin,
                MathF.Abs(ax.z) * half.x + MathF.Abs(ay.z) * half.y + MathF.Abs(az.z) * half.z + Skin);
            var inverse = rotation.Inverse;
            foreach (var body in bodies)
            {
                if (!Overlaps(body, center - extent, center + extent) || Owns(body, slot))
                    continue;
                if (SegmentBoxDistance(inverse * (body.Bottom - center), inverse * (body.Top - center), half) <= body.Radius + Skin)
                    into[body.User] = body.Root;
            }
        }
    }

    private static bool Overlaps(in Body body, float3 low, float3 high)
        => body.Max.x >= low.x && body.Min.x <= high.x && body.Max.y >= low.y && body.Min.y <= high.y
            && body.Max.z >= low.z && body.Min.z <= high.z;

    private static bool Owns(in Body body, Slot slot) => ReferenceEquals(slot, body.Root) || slot.IsDescendantOf(body.Root);

    // Everyone's capsule this frame, worked out by the first script that asks.
    private static List<Body> BodiesOf(World world)
    {
        var bodies = _bodies.GetValue(world, static _ => new Bodies());
        ulong frame = world.Time.UpdateIndex;
        if (bodies.Frame == frame)
            return bodies.List;
        bodies.Frame = frame;
        bodies.List.Clear();
        world.GetAllUsers(bodies.Users);
        foreach (var user in bodies.Users)
        {
            if (!WorldPhysics.TryGetBody(user, out var bottom, out var top, out float radius)
                || ScriptReads.RootOf(user)?.Slot is not { IsDestroyed: false } root)
                continue;
            var low = new float3(MathF.Min(bottom.x, top.x), MathF.Min(bottom.y, top.y), MathF.Min(bottom.z, top.z));
            var high = new float3(MathF.Max(bottom.x, top.x), MathF.Max(bottom.y, top.y), MathF.Max(bottom.z, top.z));
            var pad = new float3(radius, radius, radius);
            bodies.List.Add(new Body(user, root, bottom, top, radius, low - pad, high + pad));
        }
        bodies.Users.Clear();
        return bodies.List;
    }

    private static float SegmentPointDistance(float3 a, float3 b, float3 point)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared;
        float t = lengthSquared > 1e-12f ? System.Math.Clamp(float3.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return (a + ab * t - point).Length;
    }

    // From a segment to a box centred on the origin. The distance is convex along the segment, so a ternary
    // search finds its least.
    private static float SegmentBoxDistance(float3 a, float3 b, float3 half)
    {
        float low = 0f, high = 1f;
        for (int i = 0; i < 24; i++)
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
        float dx = MathF.Max(MathF.Abs(point.x) - half.x, 0f);
        float dy = MathF.Max(MathF.Abs(point.y) - half.y, 0f);
        float dz = MathF.Max(MathF.Abs(point.z) - half.z, 0f);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
