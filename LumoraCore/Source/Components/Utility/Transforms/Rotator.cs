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
