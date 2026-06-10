// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Composable turn handler owned as a field on smooth locomotion modules.
// Holds snap/smooth mode + thresholds, applies the resulting yaw delta
// through the controller's head-preserving rotation. - xlinka
public sealed class TurnSubmodule
{
    public enum Mode { Snap, Smooth }

    public Mode TurnMode { get; set; } = Mode.Snap;
    public float SnapAngle { get; set; } = 45f * 3.14159265f / 180f;
    public float SnapActivateThreshold { get; set; } = 0.8f;
    public float SnapResetThreshold { get; set; } = 0.5f;
    public float SmoothTurnSpeed { get; set; } = 90f * 3.14159265f / 180f;
    public float SmoothDeadzone { get; set; } = 0.15f;

    private LocomotionController _controller = null!;
    private bool _snapTriggered;

    public void Activate(LocomotionController controller)
    {
        _controller = controller;
        _snapTriggered = false;
    }

    public void Deactivate()
    {
        _controller = null!;
        _snapTriggered = false;
    }

    // axisValue is signed [-1, 1]. Positive = turn right.
    public void Update(float axisValue, float delta)
    {
        if (_controller == null) return;

        switch (TurnMode)
        {
            case Mode.Snap:
                UpdateSnap(axisValue);
                break;
            case Mode.Smooth:
                UpdateSmooth(axisValue, delta);
                break;
        }
    }

    private void UpdateSnap(float axisValue)
    {
        float abs = axisValue < 0 ? -axisValue : axisValue;

        if (!_snapTriggered && abs > SnapActivateThreshold)
        {
            float dir = axisValue < 0 ? -1f : 1f;
            _controller.ApplySnapTurn(-dir * SnapAngle);
            _snapTriggered = true;
        }

        if (_snapTriggered && abs < SnapResetThreshold)
            _snapTriggered = false;
    }

    private void UpdateSmooth(float axisValue, float delta)
    {
        float abs = axisValue < 0 ? -axisValue : axisValue;
        if (abs < SmoothDeadzone) return;

        float magnitude = (abs - SmoothDeadzone) / (1f - SmoothDeadzone);
        if (magnitude > 1f) magnitude = 1f;

        float dir = axisValue < 0 ? -1f : 1f;
        _controller.ApplySnapTurn(-dir * magnitude * SmoothTurnSpeed * delta);
    }
}
