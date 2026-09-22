// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
