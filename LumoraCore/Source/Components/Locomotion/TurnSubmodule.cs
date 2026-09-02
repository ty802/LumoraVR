// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Composable turn handler owned as a field on smooth locomotion modules.
// Holds snap/smooth mode + thresholds, applies the resulting yaw delta
// through the controller's head-preserving rotation. - xlinka
public sealed class TurnSubmodule
{
    public enum Mode { Snap, Smooth }

    private const float DegToRad = 3.14159265f / 180f;

    // Mode, snap step and smooth rate are COMFORT settings, so they come from the user's own
    // preferences rather than from whoever built the module - read live, so the settings screen
    // applies without respawning locomotion. Everything below is in radians; the settings are in
    // degrees because that is what a person picks. -xlinka
    public Mode TurnMode => EngineSettings.TurnMode == EngineSettings.TurnStyle.Smooth ? Mode.Smooth : Mode.Snap;
    public float SnapAngle => EngineSettings.SnapTurnAngle * DegToRad;
    public float SmoothTurnSpeed => EngineSettings.SmoothTurnSpeed * DegToRad;

    public float SnapActivateThreshold { get; set; } = 0.8f;
    public float SnapResetThreshold { get; set; } = 0.5f;
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
