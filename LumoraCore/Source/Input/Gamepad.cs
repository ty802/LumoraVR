// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input.Actions;
using Lumora.Core.Math;

namespace Lumora.Core.Input;

// A standard dual-stick gamepad, in the layout every mainstream pad reports through SDL: two sticks
// with clicks, two analog triggers, two shoulders, four face buttons, a d-pad, start/back/guide.
// Buttons are named by POSITION (FaceDown, FaceRight) rather than by one vendor's letters. An Xbox
// pad's A, a DualShock's Cross and a Switch pad's B all land on FaceDown, so a binding made on one
// pad is correct on the next one somebody plugs in. The controls screen prints both letterings.
//
// Only one pad is live at a time. Splitting a single avatar between two pads is not a thing, and
// silently summing two pads' sticks would make a stuck second controller impossible to diagnose.
// -xlinka
public class Gamepad : InputDevice
{
    public readonly Digital FaceDown = new Digital();
    public readonly Digital FaceRight = new Digital();
    public readonly Digital FaceLeft = new Digital();
    public readonly Digital FaceUp = new Digital();
    public readonly Digital LeftShoulder = new Digital();
    public readonly Digital RightShoulder = new Digital();
    public readonly Digital LeftStickPress = new Digital();
    public readonly Digital RightStickPress = new Digital();
    public readonly Digital Start = new Digital();
    public readonly Digital Back = new Digital();
    public readonly Digital Guide = new Digital();
    public readonly Digital DPadUp = new Digital();
    public readonly Digital DPadDown = new Digital();
    public readonly Digital DPadLeft = new Digital();
    public readonly Digital DPadRight = new Digital();

    // Triggers are analog and ALSO answer as digital past the press point, so a binding can treat RT
    // as either a button or a throttle without the map having to know which.
    public readonly Digital LeftTriggerButton = new Digital();
    public readonly Digital RightTriggerButton = new Digital();

    public readonly Analog LeftTrigger = new Analog();
    public readonly Analog RightTrigger = new Analog();

    public readonly Analog2D LeftStick = new Analog2D();
    public readonly Analog2D RightStick = new Analog2D();

    public bool IsConnected { get; set; }

    public string DeviceName { get; set; } = "None";

    // -1 when nothing is connected.
    public int SlotIndex { get; set; } = -1;

    public const float TriggerPressPoint = 0.5f;

    private readonly Dictionary<string, Digital> _buttonsById = new();

    private float _rumbleWeak;
    private float _rumbleStrong;
    private float _rumbleDuration;
    private bool _rumblePending;

    public override void Initialize(InputInterface input, int deviceIndex, string name)
    {
        base.Initialize(input, deviceIndex, name);

        RegisterProperty(FaceDown);
        RegisterProperty(FaceRight);
        RegisterProperty(FaceLeft);
        RegisterProperty(FaceUp);
        RegisterProperty(LeftShoulder);
        RegisterProperty(RightShoulder);
        RegisterProperty(LeftStickPress);
        RegisterProperty(RightStickPress);
        RegisterProperty(Start);
        RegisterProperty(Back);
        RegisterProperty(Guide);
        RegisterProperty(DPadUp);
        RegisterProperty(DPadDown);
        RegisterProperty(DPadLeft);
        RegisterProperty(DPadRight);
        RegisterProperty(LeftTriggerButton);
        RegisterProperty(RightTriggerButton);
        RegisterProperty(LeftTrigger);
        RegisterProperty(RightTrigger);
        RegisterProperty(LeftStick);
        RegisterProperty(RightStick);

        _buttonsById[InputControlCatalog.PadFaceDown] = FaceDown;
        _buttonsById[InputControlCatalog.PadFaceRight] = FaceRight;
        _buttonsById[InputControlCatalog.PadFaceLeft] = FaceLeft;
        _buttonsById[InputControlCatalog.PadFaceUp] = FaceUp;
        _buttonsById[InputControlCatalog.PadLeftShoulder] = LeftShoulder;
        _buttonsById[InputControlCatalog.PadRightShoulder] = RightShoulder;
        _buttonsById[InputControlCatalog.PadLeftStickPress] = LeftStickPress;
        _buttonsById[InputControlCatalog.PadRightStickPress] = RightStickPress;
        _buttonsById[InputControlCatalog.PadStart] = Start;
        _buttonsById[InputControlCatalog.PadBack] = Back;
        _buttonsById[InputControlCatalog.PadGuide] = Guide;
        _buttonsById[InputControlCatalog.PadDPadUp] = DPadUp;
        _buttonsById[InputControlCatalog.PadDPadDown] = DPadDown;
        _buttonsById[InputControlCatalog.PadDPadLeft] = DPadLeft;
        _buttonsById[InputControlCatalog.PadDPadRight] = DPadRight;
        _buttonsById[InputControlCatalog.PadLeftTrigger] = LeftTriggerButton;
        _buttonsById[InputControlCatalog.PadRightTrigger] = RightTriggerButton;
    }

    // Null when the id is not a button.
    public Digital? GetButton(string controlId)
        => controlId != null && _buttonsById.TryGetValue(controlId, out var button) ? button : null;

    // Drivers call this after writing the analog values.
    public void UpdateTriggerButtons()
    {
        LeftTriggerButton.UpdateState(LeftTrigger.Value >= TriggerPressPoint);
        RightTriggerButton.UpdateState(RightTrigger.Value >= TriggerPressPoint);
    }

    public void ClearState(float deltaTime)
    {
        foreach (var button in _buttonsById.Values)
            button.UpdateState(false);
        LeftTrigger.UpdateValue(0f, deltaTime);
        RightTrigger.UpdateValue(0f, deltaTime);
        LeftStick.UpdateValue(float2.Zero, deltaTime);
        RightStick.UpdateValue(float2.Zero, deltaTime);
    }

    public void Rumble(float weak, float strong, float duration)
    {
        _rumbleWeak = weak;
        _rumbleStrong = strong;
        _rumbleDuration = duration;
        _rumblePending = true;
    }

    public bool TryConsumeRumble(out float weak, out float strong, out float duration)
    {
        weak = _rumbleWeak;
        strong = _rumbleStrong;
        duration = _rumbleDuration;
        bool pending = _rumblePending;
        _rumblePending = false;
        return pending;
    }
}

public interface IGamepadDriver
{
    string ActivePadName { get; }

    int ConnectedPadCount { get; }

    void UpdateGamepad(Gamepad gamepad, float deltaTime);
}
