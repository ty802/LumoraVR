// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives this slot's rotation so it faces a user: the one viewing this peer, the closest one, or a
// named one.
//
// In LocalUser mode every peer turns the slot toward its own viewer, so no two people see it pointing
// the same way. That divergence is only safe because a driven value is excluded from field sync in
// both directions, so one viewer's facing can never be broadcast over another's. -xlinka
[ComponentCategory("Utility/Transforms")]
public class FaceUser : Component
{
    public readonly Sync<FacingUserMode> Mode;

    // The user faced in SpecificUser mode.
    public readonly SyncRef<User> User;

    // Face the head rather than the body root.
    public readonly Sync<bool> FaceHead;

    // Turn only around the world up axis, so the slot never tips.
    public readonly Sync<bool> YawOnly;

    // Reference up axis for the roll of the facing rotation.
    public readonly Sync<float3> Up;

    // Applied after the facing rotation, for content whose front is not local +Z.
    public readonly Sync<floatQ> RotationOffset;

    // Defaults to this slot's local rotation.
    public readonly FieldDrive<floatQ> Rotation;

    private readonly NearestUserTracker _nearest = new();

    public FaceUser()
    {
        Mode = new Sync<FacingUserMode>(this, FacingUserMode.LocalUser);
        User = new SyncRef<User>(this);
        FaceHead = new Sync<bool>(this, true);
        YawOnly = new Sync<bool>(this, true);
        Up = new Sync<float3>(this, float3.Up);
        RotationOffset = new Sync<floatQ>(this, floatQ.Identity);
        Rotation = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
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

        var parent = Slot?.Parent;
        if (parent == null)
            return;

        var from = Slot!.GlobalPosition;
        var root = _nearest.Resolve(World, in from, delta, Mode.Value, User.Target);
        if (root == null)
            return;

        var node = FaceHead.Value ? root.HeadSlot : root.Slot;
        if (node == null || node.IsDestroyed)
            return;

        if (!UserFacing.TryLookRotation(from, node.GlobalPosition, Up.Value, YawOnly.Value, out var global))
            return;

        Rotation.SetValue(parent.GlobalRotationToLocal(global) * RotationOffset.Value);
    }
}
