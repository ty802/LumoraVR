// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Numerics;

namespace Lumora.Core.Input;

public enum VRControllerSide
{
    Left,
    Right
}

public class VRController : InputDevice
{
    public VRControllerSide Side { get; }

    public string DeviceName { get; set; } = "Unknown Controller";

    // 0 to 1, -1 when the runtime does not report it.
    public float BatteryLevel { get; set; } = -1f;

    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 AngularVelocity { get; set; }

    public bool TriggerPressed { get; set; }
    public float TriggerValue { get; set; }

    // The analog squeeze is finger curl, not a press. On a handle with a force sensor it sits well
    // above zero from just holding the controller, so the press comes off the force there and off
    // the value only where no force exists. Both go through the same two thresholds. -xlinka
    // 0.6 / 0.4 read as a hard squeeze on the force handle; testers wanted less. The gap between the
    // two is what keeps a resting hand from chattering, so keep it wide when tuning. -xlinka
    public const float GripPressThreshold = 0.45f;
    public const float GripReleaseThreshold = 0.28f;

    public float GripValue { get; set; }
    public float GripForce { get; set; }
    public bool HasGripForce { get; set; }
    public bool GripPressed { get; private set; }

    public float Trigger => TriggerValue;
    public float Grip => GripValue;

    public Vector2 ThumbstickPosition { get; set; }
    public bool ThumbstickPressed { get; set; }
    public bool ThumbstickTouched { get; set; }

    public Vector2 TouchpadPosition { get; set; }
    public bool TouchpadPressed { get; set; }
    public bool TouchpadTouched { get; set; }

    // A finger resting on the trigger or on a face button, where the controller senses touch. Off on
    // controllers that do not.
    public bool TriggerTouched { get; set; }
    public bool PrimaryButtonTouched { get; set; }
    public bool SecondaryButtonTouched { get; set; }

    // A/X, B/Y, and the app-facing menu button where the profile has one.
    public bool PrimaryButtonPressed { get; set; }
    public bool SecondaryButtonPressed { get; set; }
    public bool MenuButtonPressed { get; set; }

    // True when the bound profile has a primary face button. Without one the menu button carries
    // both the dash (hold) and the context menu (tap) through the charge below.
    public bool HasFaceButtons { get; set; }

    public float MenuCharge => _menuCharge.Charge;
    public bool MenuHoldFired { get; private set; }
    public bool MenuTapFired { get; private set; }

    private PressLatch _grip;
    private MenuChargeLatch _menuCharge;

    private float _hapticAmplitude;
    private float _hapticDuration;
    private float _hapticFrequency = 1000f;

    public VRController(VRControllerSide side)
    {
        Side = side;
    }

    // Once per frame after the driver has written this frame's hardware and before actions read it.
    // Drivers write raw values only; every edge that needs memory is made here, so a driver that
    // runs twice a frame cannot double-step it.
    public void Advance(float deltaTime, VRController? otherHand)
    {
        float gripSource = HasGripForce ? GripForce : GripValue;
        GripPressed = _grip.Update(gripSource, GripPressThreshold, GripReleaseThreshold);

        bool sharesMenuAndDash = !HasFaceButtons;
        bool otherCharging = otherHand != null && otherHand.MenuCharge > 0f;
        _menuCharge.Update(sharesMenuAndDash && IsDeviceActive && MenuButtonPressed, deltaTime, otherCharging);
        MenuHoldFired = _menuCharge.HoldFired;
        MenuTapFired = _menuCharge.TapFired;
    }

    public void ResetLatches()
    {
        _grip.Reset();
        _menuCharge.Reset();
        GripPressed = false;
        MenuHoldFired = false;
        MenuTapFired = false;
    }

    public void TriggerHaptic(float amplitude, float duration, float frequency = 1000f)
    {
        _hapticAmplitude = amplitude;
        _hapticDuration = duration;
        _hapticFrequency = frequency;
    }

    public (float amplitude, float duration, float frequency) GetPendingHaptic()
    {
        var result = (_hapticAmplitude, _hapticDuration, _hapticFrequency);
        _hapticAmplitude = 0;
        _hapticDuration = 0;
        return result;
    }

    public bool IsTracked { get; set; }
}
