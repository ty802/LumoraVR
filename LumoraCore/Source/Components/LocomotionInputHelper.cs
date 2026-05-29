// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Helpers that all smooth locomotion modules need: read a unified movement
// axis regardless of whether the user is on stick or keyboard, plus the
// per-axis filter that matches the ref pattern (exclusive snap to whichever
// component is dominant). - xlinka
public static class LocomotionInputHelper
{
    // VR stick + keyboard WASD collapsed to one float2. Stick wins when the
    // stick has signal; otherwise WASD. This is the abstraction barrier that
    // lets PhysicalLocomotion read movement without caring about the source.
    public static float2 ReadMovementAxis(InputInterface input, IKeyboardDriver keyboard)
    {
        if (input?.IsDashboardOpen == true)
            return float2.Zero;

        if (input?.LeftController is { } left && left.IsTracked)
        {
            var stick = left.ThumbstickPosition;
            if (stick.X * stick.X + stick.Y * stick.Y > 0.0001f)
                return new float2(stick.X, stick.Y);
        }

        if (keyboard != null)
        {
            float x = 0f, y = 0f;
            if (keyboard.GetKeyState(Key.W)) y += 1f;
            if (keyboard.GetKeyState(Key.S)) y -= 1f;
            if (keyboard.GetKeyState(Key.A)) x -= 1f;
            if (keyboard.GetKeyState(Key.D)) x += 1f;
            return new float2(x, y);
        }

        return float2.Zero;
    }

    // Right-stick yaw axis. Desktop has no equivalent here because mouse-look
    // drives yaw on the controller directly, not through a module-owned turn.
    public static float ReadTurnAxis(InputInterface input)
    {
        if (input?.IsDashboardOpen == true)
            return 0f;
        if (input?.RightController is { } right && right.IsTracked)
            return right.ThumbstickPosition.X;
        return 0f;
    }

    public static bool ReadJump(InputInterface input, IKeyboardDriver keyboard)
    {
        if (input?.IsDashboardOpen == true) return false;
        if (input?.LeftController?.PrimaryButtonPressed == true) return true;
        if (input?.RightController?.PrimaryButtonPressed == true) return true;
        return keyboard?.GetKeyState(Key.Space) ?? false;
    }

    public static bool ReadCrouch(IKeyboardDriver keyboard)
    {
        if (keyboard == null) return false;
        return keyboard.GetKeyState(Key.LeftControl) || keyboard.GetKeyState(Key.C);
    }

    public static bool ReadSprint(IKeyboardDriver keyboard)
    {
        if (keyboard == null) return false;
        return keyboard.GetKeyState(Key.LeftShift) || keyboard.GetKeyState(Key.RightShift);
    }

    // Lock the input vector to its dominant axis. Matches the ref
    // FilterLocomotionAxis when exclusive mode is on: prevents diagonal
    // drift on cheap thumbsticks. - xlinka
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
