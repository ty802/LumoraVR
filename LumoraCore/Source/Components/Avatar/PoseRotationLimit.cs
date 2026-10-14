// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Limits how far a body pose may turn: first the swing that carries Axis away from itself, then the twist
// about it, read against TwistAxis. Both axes are in this slot's space, both limits are degrees, and zero
// allows none at all; a twist limit of 180 leaves the twist alone. -xlinka
[ComponentCategory("Users/Avatar")]
public class PoseRotationLimit : Component, IPoseFilter
{
    public readonly Sync<float> MaxTwist = new(0f);

    public readonly Sync<float> MaxSwing = new(0f);

    public readonly Sync<float3> Axis = new(float3.Backward);

    public readonly Sync<float3> TwistAxis = new(float3.Right);

    private const float RadToDeg = 180f / MathF.PI;

    public void ProcessPose(AvatarSocket socket, Slot space, ref float3 position, ref floatQ rotation, ref bool isTracking)
    {
        var slot = Slot;
        if (!Enabled.Value || slot == null || slot.IsDestroyed || space == null)
            return;

        float3 axis = slot.LocalDirectionToSpace(Axis.Value, space);
        float3 twistAxis = slot.LocalDirectionToSpace(TwistAxis.Value, space);
        rotation = LimitSwing(rotation, axis, MaxSwing.Value);
        rotation = LimitTwist(rotation, axis, twistAxis, MaxTwist.Value);
    }

    // The swing is the shortest turn from the axis to where the rotation carries it; anything past the limit
    // is turned back along that same arc.
    public static floatQ LimitSwing(floatQ rotation, float3 axis, float maxDegrees)
    {
        if (axis.LengthSquared < 1e-12f)
            return rotation;
        axis = axis.Normalized;
        float3 carried = rotation * axis;
        floatQ swing = FabrikSolver.FromToRotation(axis, carried);
        floatQ limited = RotateTowards(floatQ.Identity, swing, maxDegrees);
        return (FabrikSolver.FromToRotation(carried, limited * axis) * rotation).Normalized;
    }

    // The twist is what is left of the rotation about the axis once the swing is taken out; the twist axis,
    // laid flat against the turned axis, says where no twist is.
    public static floatQ LimitTwist(floatQ rotation, float3 axis, float3 reference, float maxDegrees)
    {
        if (maxDegrees >= 180f)
            return rotation;
        maxDegrees = System.Math.Clamp(maxDegrees, 0f, 180f);
        float3 normal = rotation * axis;
        float3 untwisted = reference;
        Orthonormalize(ref normal, ref untwisted);
        float3 twisted = rotation * reference;
        Orthonormalize(ref normal, ref twisted);
        floatQ none = (FabrikSolver.FromToRotation(twisted, untwisted) * rotation).Normalized;
        return maxDegrees == 0f ? none : RotateTowards(none, rotation, maxDegrees);
    }

    private static floatQ RotateTowards(floatQ from, floatQ to, float maxDegrees)
    {
        float angle = MathF.Acos(MathF.Min(MathF.Abs(floatQ.Dot(from, to)), 1f)) * 2f * RadToDeg;
        if (angle <= 0f)
            return from;
        return floatQ.Slerp(from, to, MathF.Min(1f, maxDegrees / angle)).Normalized;
    }

    private static void Orthonormalize(ref float3 normal, ref float3 tangent)
    {
        normal = normal.LengthSquared < 1e-12f ? float3.Right : normal.Normalized;
        float3 side = float3.Cross(normal, tangent);
        if (side.LengthSquared < 1e-12f)
            side = float3.Cross(normal, new float3(normal.z, -normal.x, normal.y));
        tangent = float3.Cross(side, normal).Normalized;
    }
}
