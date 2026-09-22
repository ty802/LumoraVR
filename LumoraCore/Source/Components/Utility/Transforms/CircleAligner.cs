// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// Lays children out around a circle, optionally turning each one to follow it.
[ComponentCategory("Utility/Transforms")]
public class CircleAligner : ChildLayoutBase
{
    private const float Deg2Rad = System.MathF.PI / 180f;

    public readonly Sync<float3> Axis;

    public readonly Sync<float> Radius;

    // Degrees, where the first child sits.
    public readonly Sync<float> StartAngle;

    // Degrees covered by the whole ring.
    public readonly Sync<float> Arc;

    // Put the last child at the end of the arc rather than one step short of it. No effect on a full
    // circle, where the two would land on top of each other.
    public readonly Sync<bool> FillWholeArc;

    public readonly Sync<bool> DriveRotation;

    // Degrees added to each child's rotation.
    public readonly Sync<float> RotationOffset;

    public CircleAligner()
    {
        Axis = new Sync<float3>(this, float3.Up);
        Radius = new Sync<float>(this, 1f);
        StartAngle = new Sync<float>(this, 0f);
        Arc = new Sync<float>(this, 360f);
        FillWholeArc = new Sync<bool>(this, false);
        DriveRotation = new Sync<bool>(this, false);
        RotationOffset = new Sync<float>(this, 0f);
    }

    public override void OnUpdate(float delta)
    {
        var items = OrderedItems;
        int count = items.Count;
        if (count == 0)
            return;

        var axis = Axis.Value;
        if (axis.LengthSquared <= 0f)
            axis = float3.Up;
        axis = axis.Normalized;

        var spoke = Orthogonal(axis) * Radius.Value;

        int steps = FillWholeArc.Value && count > 1 ? count - 1 : count;
        float start = StartAngle.Value * Deg2Rad;
        float step = Arc.Value / steps * Deg2Rad;
        float rotationOffset = RotationOffset.Value * Deg2Rad;
        bool driveRotation = DriveRotation.Value;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            float angle = start + step * i;
            var turn = floatQ.AxisAngleRad(axis, angle);

            if (item.Position.IsLinkValid)
                Push(item.Position, turn * spoke);

            var root = item.Root.Target;
            if (root == null || root.IsDestroyed)
                continue;

            if (driveRotation)
            {
                if (!ReferenceEquals(item.Rotation.Target, root.LocalRotation))
                    item.Rotation.DriveTarget(root.LocalRotation);
                if (item.Rotation.IsLinkValid)
                    Push(item.Rotation, floatQ.AxisAngleRad(axis, angle + rotationOffset));
            }
            else if (item.Rotation.HasTarget)
            {
                item.Rotation.ReleaseLink();
            }
        }
    }

    // Any direction perpendicular to the axis will do for the start of the ring; picking the world axis
    // the ring axis is least aligned with keeps the cross product away from zero.
    private static float3 Orthogonal(float3 axis)
    {
        var reference = System.Math.Abs(axis.y) < 0.9f ? float3.Up : float3.Forward;
        var ortho = float3.Cross(reference, axis);
        return ortho.LengthSquared > 0f ? ortho.Normalized : float3.Right;
    }
}
