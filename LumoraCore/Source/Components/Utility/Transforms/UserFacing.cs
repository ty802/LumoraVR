// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

public static class UserFacing
{
    // Returns a global rotation that points the slot's local +Z at target. up falls back to world up
    // when parallel to the view direction; yawOnly flattens the direction so the slot turns but never
    // tips. False when from and target coincide and there is no direction to face.
    //
    // Built by hand rather than through floatQ.LookRotation, which composes its matrix from basis ROWS
    // and so returns the INVERSE of the rotation it names: headings come out negated and planes go
    // edge-on at oblique angles. Nothing that has to face something should use it. -xlinka
    public static bool TryLookRotation(in float3 from, in float3 target, in float3 up, bool yawOnly, out floatQ rotation)
    {
        var forward = target - from;
        if (yawOnly)
            forward.y = 0f;

        if (forward.LengthSquared < 1e-8f)
        {
            rotation = floatQ.Identity;
            return false;
        }

        if (yawOnly)
        {
            rotation = floatQ.AxisAngle(float3.Up, MathF.Atan2(forward.x, forward.z));
            return true;
        }

        forward = forward.Normalized;
        var reference = up.LengthSquared < 1e-8f ? float3.Up : up.Normalized;
        var right = float3.Cross(reference, forward);
        if (right.LengthSquared < 1e-8f)
        {
            // Looking straight along the up axis leaves no unique roll. Pick any perpendicular rather
            // than handing back a degenerate basis.
            reference = MathF.Abs(forward.y) > 0.9f ? float3.Forward : float3.Up;
            right = float3.Cross(reference, forward);
        }
        right = right.Normalized;
        var realUp = float3.Cross(forward, right);

        rotation = FromBasis(right, realUp, forward);
        return true;
    }

    // By head position. Null when nobody has a body yet.
    public static UserRoot? FindNearestUserRoot(World? world, in float3 from)
    {
        if (world == null)
            return null;

        UserRoot? best = null;
        float bestDistance = float.MaxValue;
        foreach (var user in world.GetAllUsers())
        {
            var root = user?.Root;
            var head = root?.HeadSlot;
            if (head == null || head.IsDestroyed)
                continue;
            float distance = (head.GlobalPosition - from).LengthSquared;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = root;
            }
        }
        return best;
    }

    // Column-major basis: each axis is a COLUMN, which is what actually maps local axes onto the given
    // world axes. Writing them as rows is the mistake floatQ.LookRotation makes.
    private static floatQ FromBasis(in float3 right, in float3 up, in float3 forward)
    {
        float trace = right.x + up.y + forward.z;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            return new floatQ(
                (up.z - forward.y) / s,
                (forward.x - right.z) / s,
                (right.y - up.x) / s,
                0.25f * s).Normalized;
        }
        if (right.x > up.y && right.x > forward.z)
        {
            float s = MathF.Sqrt(1f + right.x - up.y - forward.z) * 2f;
            return new floatQ(
                0.25f * s,
                (up.x + right.y) / s,
                (forward.x + right.z) / s,
                (up.z - forward.y) / s).Normalized;
        }
        if (up.y > forward.z)
        {
            float s = MathF.Sqrt(1f + up.y - right.x - forward.z) * 2f;
            return new floatQ(
                (up.x + right.y) / s,
                0.25f * s,
                (forward.y + up.z) / s,
                (forward.x - right.z) / s).Normalized;
        }
        else
        {
            float s = MathF.Sqrt(1f + forward.z - right.x - up.y) * 2f;
            return new floatQ(
                (forward.x + right.z) / s,
                (forward.y + up.z) / s,
                0.25f * s,
                (right.y - up.x) / s).Normalized;
        }
    }
}

// Which user a facing or placement component follows.
public enum FacingUserMode
{
    // The user viewing this peer, so every viewer gets their own result.
    LocalUser,
    // Whichever user is closest right now.
    NearestUser,
    // One named user.
    SpecificUser,
}

// Keeps the closest user to a point without rescanning every frame.
//
// World.GetAllUsers allocates a fresh list per call, so a per-frame nearest-user scan on every
// component that wants one is a steady stream of garbage for an answer that changes at walking pace.
// Rescan a few times a second and hold the result. Per-peer state, never replicated. -xlinka
public sealed class NearestUserTracker
{
    private const float RescanInterval = 0.25f;

    private UserRoot? _root;
    private float _countdown;

    public UserRoot? Resolve(World? world, in float3 from, float delta, FacingUserMode mode, User? explicitUser)
    {
        switch (mode)
        {
            case FacingUserMode.SpecificUser:
                return explicitUser?.Root;
            case FacingUserMode.NearestUser:
                _countdown -= delta;
                if (_countdown <= 0f || _root == null || _root.IsDestroyed)
                {
                    _countdown = RescanInterval;
                    _root = UserFacing.FindNearestUserRoot(world, in from);
                }
                return _root;
            default:
                return world?.LocalUser?.Root;
        }
    }
}
