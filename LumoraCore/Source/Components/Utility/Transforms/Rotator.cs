// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives this slot's rotation as a constant spin around an axis.
//
// The angle is computed from the shared clock rather than accumulated per frame. Accumulating drifts
// apart between peers (and between a paused window and a running one) with no way back, while a
// computed angle means a late joiner sees the spinner exactly where everyone else does. -xlinka
[ComponentCategory("Utility/Transforms")]
public class Rotator : Component
{
    // In the slot's parent space.
    public readonly Sync<float3> Axis;

    // Degrees per second. Negative spins the other way.
    public readonly Sync<float> Speed;

    // The rotation the spin is applied on top of.
    public readonly Sync<floatQ> BaseRotation;

    // Defaults to this slot's local rotation.
    public readonly FieldDrive<floatQ> Rotation;

    public Rotator()
    {
        Axis = new Sync<float3>(this, float3.Up);
        Speed = new Sync<float>(this, 45f);
        BaseRotation = new Sync<floatQ>(this, floatQ.Identity);
        Rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
    }

    public override void OnAttach()
    {
        base.OnAttach();
        // Spin from wherever the slot already sits, so attaching this never snaps it to identity.
        BaseRotation.Value = Slot.LocalRotation.Value;
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Rotation.ShouldApplyDefault)
            Rotation.DriveTarget(Slot.LocalRotation);
    }

    public override void OnUpdate(float delta)
    {
        if (!Rotation.IsLinkValid)
            return;

        var axis = Axis.Value;
        if (axis.LengthSquared < 1e-8f)
            return;

        // Wrap before converting: at speed 1 the raw product grows past float precision within a day of
        // wall clock and the spin visibly quantises.
        double degrees = UtilityClock.Seconds(World) * Speed.Value % 360.0;
        float radians = (float)(degrees * System.Math.PI / 180.0);
        Rotation.SetValue(BaseRotation.Value * floatQ.AxisAngle(axis, radians));
    }
}

// Drives this slot's position and rotation with a slow random wander around its resting pose.
//
// Value noise, not a per-frame random walk: the offset is a pure function of the shared clock and the
// seed, so every peer sees the same wander and nothing has to be replicated per frame. A random walk
// would diverge immediately and could never be re-derived by a joining peer. -xlinka
[ComponentCategory("Utility/Transforms")]
public class Jitter : Component
{
    // How far the position wanders, per axis, in the slot's parent space.
    public readonly Sync<float3> PositionMagnitude;

    // How far the rotation wanders, per axis, in degrees.
    public readonly Sync<float3> RotationMagnitude;

    // Wanders per second, per axis.
    public readonly Sync<float3> Speed;

    // Changes the pattern without changing its character.
    public readonly Sync<int> Seed;

    // The position the wander is applied on top of.
    public readonly Sync<float3> BasePosition;

    // The rotation the wander is applied on top of.
    public readonly Sync<floatQ> BaseRotation;

    // Defaults to this slot's local position.
    public readonly FieldDrive<float3> Position;

    // Defaults to this slot's local rotation.
    public readonly FieldDrive<floatQ> Rotation;

    public Jitter()
    {
        PositionMagnitude = new Sync<float3>(this, float3.Zero);
        RotationMagnitude = new Sync<float3>(this, new float3(5f, 5f, 5f));
        Speed = new Sync<float3>(this, float3.One);
        Seed = new Sync<int>(this, 0);
        BasePosition = new Sync<float3>(this, float3.Zero);
        BaseRotation = new Sync<floatQ>(this, floatQ.Identity);
        Position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
    }

    public override void OnAttach()
    {
        base.OnAttach();
        BasePosition.Value = Slot.LocalPosition.Value;
        BaseRotation.Value = Slot.LocalRotation.Value;
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Position.ShouldApplyDefault)
            Position.DriveTarget(Slot.LocalPosition);
        if (Rotation.ShouldApplyDefault)
            Rotation.DriveTarget(Slot.LocalRotation);
    }

    public override void OnUpdate(float delta)
    {
        double now = UtilityClock.Seconds(World);
        var speed = Speed.Value;
        int seed = Seed.Value;

        var wander = new float3(
            SmoothNoise(now * speed.x, seed),
            SmoothNoise(now * speed.y, seed + 8191),
            SmoothNoise(now * speed.z, seed + 16381));

        if (Position.IsLinkValid)
            Position.SetValue(BasePosition.Value + wander * PositionMagnitude.Value);

        if (Rotation.IsLinkValid)
        {
            var degrees = wander * RotationMagnitude.Value;
            var euler = degrees * (float)(System.Math.PI / 180.0);
            Rotation.SetValue(BaseRotation.Value * floatQ.Euler(euler));
        }
    }

    // Value noise in [-1, 1]: hash the two integer samples either side and ease between them. Cheap,
    // deterministic from the seed alone, and continuous, which is what separates a wander from a
    // twitch.
    private static float SmoothNoise(double t, int seed)
    {
        long cell = (long)System.Math.Floor(t);
        float fraction = (float)(t - cell);
        float eased = fraction * fraction * (3f - 2f * fraction);
        float a = Hash(cell, seed);
        float b = Hash(cell + 1, seed);
        return a + (b - a) * eased;
    }

    private static float Hash(long cell, int seed)
    {
        ulong h = (ulong)cell * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed * 0xBF58476D1CE4E5B9UL;
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 29;
        // Top 24 bits are the best mixed, and 2^24 is exactly representable in a float.
        return (h >> 40) / (float)(1 << 23) - 1f;
    }
}
