// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Movement reads for the locomotion modules, plus a per-axis filter (exclusive snap to whichever
// component is dominant).
//
// These used to collapse "stick or keyboard?" themselves. They no longer do: the action map already
// merged every source before any module ran, so a module gets one number and never learns whether it
// came from a key, a thumbstick or a pad. Which control feeds which action is a user setting now,
// and adding a device is a line in the default map rather than a branch in here.
//
// Whether locomotion is allowed at all is also not decided here any more - the locomotion action set
// is gated off while the dashboard is up, so these return rest without having to ask. - xlinka
public static class LocomotionInputHelper
{
    public static float2 ReadMovementAxis(InputInterface input)
        => input?.Actions?.Locomotion.Move.Value ?? float2.Zero;

    // Turn axis. Desktop leaves this at zero because mouse-look drives yaw on the controller
    // directly, not through a module-owned turn.
    public static float ReadTurnAxis(InputInterface input)
        => input?.Actions?.Locomotion.Turn.Value ?? 0f;

    // Vertical fly axis for 3-axis modes (noclip): +1 up, -1 down.
    public static float ReadVerticalAxis(InputInterface input)
        => input?.Actions?.Locomotion.Fly.Value ?? 0f;

    public static bool ReadJump(InputInterface input)
        => input?.Actions?.Locomotion.Jump.Held == true;

    public static bool ReadCrouch(InputInterface input)
        => input?.Actions?.Locomotion.Crouch.Held == true;

    public static bool ReadSprint(InputInterface input)
        => input?.Actions?.Locomotion.Sprint.Held == true;

    // Lock the input vector to its dominant axis. Exclusive mode prevents
    // diagonal drift on cheap thumbsticks. - xlinka
    public static float2 SnapToDominantAxis(float2 axis)
    {
        float absX = axis.x < 0 ? -axis.x : axis.x;
        float absY = axis.y < 0 ? -axis.y : axis.y;
        return absY > absX ? new float2(0f, axis.y) : new float2(axis.x, 0f);
    }

    public static float2 ApplyDeadzone(float2 axis, float deadzone)
    {
        if (axis.LengthSquared < deadzone * deadzone) return float2.Zero;
        if (axis.LengthSquared > 1f) return axis.Normalized;
        return axis;
    }
}
