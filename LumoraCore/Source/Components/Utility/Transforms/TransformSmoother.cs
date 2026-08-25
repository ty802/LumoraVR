// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives this slot's transform to trail another slot's pose with damping, instead of snapping onto it.
//
// Positive update order on purpose: the source has to have moved before the follower reads it, or the
// follower chases last frame's pose and lags by a frame on top of the damping it is supposed to add.
//
// Rates are exp-based so the same setting converges at the same real speed at 30fps and at 144fps.
// -xlinka
[ComponentCategory("Utility/Transforms")]
[DefaultUpdateOrder(100)]
public class TransformSmoother : Component
{
    public readonly SyncRef<Slot> Source;

    // In the source's own space.
    public readonly Sync<float3> Offset;

    // Applied after the source's rotation.
    public readonly Sync<floatQ> RotationOffset;

    // Zero pins the follower where it is.
    public readonly Sync<float> PositionSpeed;

    // Zero pins the follower where it is.
    public readonly Sync<float> RotationSpeed;

    // Zero pins the follower where it is.
    public readonly Sync<float> ScaleSpeed;

    public readonly Sync<bool> FollowPosition;

    public readonly Sync<bool> FollowRotation;

    public readonly Sync<bool> FollowScale;

    // Defaults to this slot's local position.
    public readonly FieldDrive<float3> Position;

    // Defaults to this slot's local rotation.
    public readonly FieldDrive<floatQ> Rotation;

    // Defaults to this slot's local scale.
    public readonly FieldDrive<float3> Scale;

    public TransformSmoother()
    {
        Source = new SyncRef<Slot>(this);
        Offset = new Sync<float3>(this, float3.Zero);
        RotationOffset = new Sync<floatQ>(this, floatQ.Identity);
        PositionSpeed = new Sync<float>(this, 8f);
        RotationSpeed = new Sync<float>(this, 8f);
        ScaleSpeed = new Sync<float>(this, 8f);
        FollowPosition = new Sync<bool>(this, true);
        FollowRotation = new Sync<bool>(this, true);
        FollowScale = new Sync<bool>(this, false);
        Position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
        Scale = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Position.ShouldApplyDefault)
            Position.DriveTarget(Slot.LocalPosition);
        if (Rotation.ShouldApplyDefault)
            Rotation.DriveTarget(Slot.LocalRotation);
        if (Scale.ShouldApplyDefault)
            Scale.DriveTarget(Slot.LocalScale);
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var parent = Slot?.Parent;
        if (source == null || source.IsDestroyed || parent == null || delta <= 0f)
            return;

        var sourceRotation = source.GlobalRotation;

        if (FollowPosition.Value && Position.IsLinkValid)
        {
            var goal = parent.GlobalPointToLocal(source.GlobalPosition + sourceRotation * Offset.Value);
            Position.SetValue(float3.Lerp(Position.DrivenValue, goal, Factor(PositionSpeed.Value, delta)));
        }

        if (FollowRotation.Value && Rotation.IsLinkValid)
        {
            var goal = parent.GlobalRotationToLocal(sourceRotation * RotationOffset.Value);
            Rotation.SetValue(floatQ.Slerp(Rotation.DrivenValue, goal, Factor(RotationSpeed.Value, delta)));
        }

        if (FollowScale.Value && Scale.IsLinkValid)
        {
            var goal = parent.GlobalScaleToLocal(source.GlobalScale);
            Scale.SetValue(float3.Lerp(Scale.DrivenValue, goal, Factor(ScaleSpeed.Value, delta)));
        }
    }

    private static float Factor(float speed, float delta)
        => speed <= 0f ? 0f : 1f - MathF.Exp(-speed * delta);
}
