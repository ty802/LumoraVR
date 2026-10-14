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

    public float TriggerValue { get; set; }

    // The trigger's own click, and whether the bound profile has a click path of its own for it.
    // Elsewhere the click is the runtime cutting the value at a depth nothing here can see, so the deep
    // level comes off the value instead.
    public bool TriggerClick { get; set; }
    public bool HasTriggerClick { get; set; }

    // Hand tracking feeds the pinch in as the trigger, and a pinch has no deep level.
    public bool TriggerIsPinch { get; set; }

    // Latched once a frame in Advance, see TriggerLatch. Deep is only ever held under a held press.
    public bool TriggerPressed => _trigger.Held;
    public bool TriggerDeep => _trigger.Deep;
    public bool TriggerDeepPressed => _trigger.DeepPressed;
    public bool TriggerDeepReleased => _trigger.DeepReleased;

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

    // Extra controls on split gamepads such as Steam Frame. Unbound on two-face-button devices.
    public bool FaceLeftPressed { get; set; }
    public bool FaceUpPressed { get; set; }
    public bool ShoulderPressed { get; set; }
    public bool FaceLeftTouched { get; set; }
    public bool FaceUpTouched { get; set; }
    public bool ShoulderTouched { get; set; }

    // True when the bound profile has a primary face button. Without one the menu button carries
    // both the dash (hold) and the context menu (tap) through the charge below.
    public bool HasFaceButtons { get; set; }

    public float MenuCharge => _menuCharge.Charge;
    public bool MenuHoldFired { get; private set; }
    public bool MenuTapFired { get; private set; }

    private PressLatch _grip;
    private TriggerLatch _trigger;
    private MenuChargeLatch _menuCharge;

    private float _hapticAmplitude;
    private float _hapticDuration;
    private float _hapticFrequency;

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

        _trigger.Update(TriggerValue, TriggerClick, HasTriggerClick, !TriggerIsPinch, deltaTime);

        bool sharesMenuAndDash = !HasFaceButtons;
        bool otherCharging = otherHand != null && otherHand.MenuCharge > 0f;
        _menuCharge.Update(sharesMenuAndDash && IsDeviceActive && MenuButtonPressed, deltaTime, otherCharging);
        MenuHoldFired = _menuCharge.HoldFired;
        MenuTapFired = _menuCharge.TapFired;
    }

    public void ResetLatches()
    {
        _grip.Reset();
        _trigger.Reset();
        _menuCharge.Reset();
        GripPressed = false;
        MenuHoldFired = false;
        MenuTapFired = false;
    }

    // Every pulse for this hand lands here, scaled by the haptics strength in Settings, 0 being off.
    // Pulses asked for in the same frame do not overwrite each other: the strongest one plays, so a
    // hover tick beside a press cannot erase it. Frequency 0 leaves the choice to the runtime. -xlinka
    public void TriggerHaptic(float amplitude, float duration, float frequency = 0f)
    {
        amplitude *= EngineSettings.HapticsStrength;
        if (!(amplitude > 0f) || !(duration > 0f))
            return;
        if (amplitude < _hapticAmplitude || (amplitude == _hapticAmplitude && duration <= _hapticDuration))
            return;
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
