// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives this slot's position and rotation to sit on a user's body node, with an offset that scales
// with that user.
//
// The offset is applied in the node's own space and multiplied by the user's global scale, so a panel
// parked half a metre in front of the head stays half a metre in front of a shrunk or grown avatar
// instead of ending up inside their skull. -xlinka
[ComponentCategory("Utility/Transforms")]
public class PlaceAtUser : Component
{
    public readonly Sync<FacingUserMode> Mode;

    // The user followed in SpecificUser mode.
    public readonly SyncRef<User> User;

    // Sit on the head rather than the body root.
    public readonly Sync<bool> UseHead;

    // In the node's own space, scaled by the user.
    public readonly Sync<float3> Offset;

    // Applied after the node's rotation.
    public readonly Sync<floatQ> RotationOffset;

    // Take the node's rotation too, not just its position.
    public readonly Sync<bool> FollowRotation;

    // Defaults to this slot's local position.
    public readonly FieldDrive<float3> Position;

    // Defaults to this slot's local rotation.
    public readonly FieldDrive<floatQ> Rotation;

    private readonly NearestUserTracker _nearest = new();

    public PlaceAtUser()
    {
        Mode = new Sync<FacingUserMode>(this, FacingUserMode.LocalUser);
        User = new SyncRef<User>(this);
        UseHead = new Sync<bool>(this, true);
        Offset = new Sync<float3>(this, float3.Zero);
        RotationOffset = new Sync<floatQ>(this, floatQ.Identity);
        FollowRotation = new Sync<bool>(this, false);
        Position = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
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
        var parent = Slot?.Parent;
        if (parent == null)
            return;

        var from = Slot!.GlobalPosition;
        var root = _nearest.Resolve(World, in from, delta, Mode.Value, User.Target);
        if (root == null)
            return;

        var node = UseHead.Value ? root.HeadSlot : root.Slot;
        if (node == null || node.IsDestroyed)
            return;

        var nodeRotation = node.GlobalRotation;
        var target = node.GlobalPosition + nodeRotation * (Offset.Value * root.GlobalScale);
        Position.SetValue(parent.GlobalPointToLocal(target));

        if (FollowRotation.Value)
            Rotation.SetValue(parent.GlobalRotationToLocal(nodeRotation * RotationOffset.Value));
    }
}

// Drives this slot's scale so it keeps a constant apparent size for the user it follows.
//
// Anything parented outside a user (a world panel, a badge) does not inherit their scale, so a user
// who shrinks sees it grow to fill their view. Multiplying the base scale by the user's global scale
// puts it back. -xlinka
[ComponentCategory("Utility/Transforms")]
public class ScaleWithUser : Component
{
    public readonly Sync<FacingUserMode> Mode;

    // The user followed in SpecificUser mode.
    public readonly SyncRef<User> User;

    // Scale at a user scale of one.
    public readonly Sync<float3> BaseScale;

    // Defaults to this slot's local scale.
    public readonly FieldDrive<float3> Scale;

    private readonly NearestUserTracker _nearest = new();

    public ScaleWithUser()
    {
        Mode = new Sync<FacingUserMode>(this, FacingUserMode.LocalUser);
        User = new SyncRef<User>(this);
        BaseScale = new Sync<float3>(this, float3.One);
        Scale = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnAttach()
    {
        base.OnAttach();
        // Adopt whatever the slot is already sized at, so attaching this never resizes anything.
        BaseScale.Value = Slot.LocalScale.Value;
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Scale.ShouldApplyDefault)
            Scale.DriveTarget(Slot.LocalScale);
    }

    public override void OnUpdate(float delta)
    {
        if (!Scale.IsLinkValid)
            return;

        var from = Slot!.GlobalPosition;
        var root = _nearest.Resolve(World, in from, delta, Mode.Value, User.Target);
        if (root == null)
            return;

        Scale.SetValue(BaseScale.Value * root.GlobalScale);
    }
}
