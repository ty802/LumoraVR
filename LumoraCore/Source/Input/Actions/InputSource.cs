// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Input.Actions;

// Reads one device family by control id. This is the only place in the action layer that knows what
// a key or a thumbstick actually is.
public interface IInputSource
{
    InputDeviceKind Kind { get; }

    // False when the hardware is absent or something else owns it; bindings on it read as rest.
    bool IsAvailable { get; }

    bool ReadDigital(in ControlRef control);
    float ReadAnalog(in ControlRef control);
    float2 ReadAnalog2D(in ControlRef control);

    // First control that went active this frame, for the rebind listener. Returns false when
    // nothing on this device moved.
    bool TryCaptureControl(out ControlRef control);
}

public sealed class KeyboardSource : IInputSource
{
    private readonly InputInterface _input;

    // Raised while a focused text field is consuming keystrokes. Everything bound to the keyboard
    // reads as released for the duration, so typing "was" into a name box does not walk you into a
    // wall or fire a tool. Other device families are untouched: a controller still works while
    // someone types. -xlinka
    public bool TextFocusHeld { get; set; }

    public InputDeviceKind Kind => InputDeviceKind.Keyboard;
    public bool IsAvailable => !TextFocusHeld && _input?.Keyboard != null;

    public KeyboardSource(InputInterface input)
    {
        _input = input;
    }

    public bool ReadDigital(in ControlRef control)
    {
        var keyboard = _input?.Keyboard;
        if (keyboard == null || TextFocusHeld)
            return false;
        return InputControlCatalog.TryParseKey(control.Control, out var key) && keyboard.IsKeyPressed(key);
    }

    public float ReadAnalog(in ControlRef control) => ReadDigital(control) ? 1f : 0f;
    public float2 ReadAnalog2D(in ControlRef control) => float2.Zero;

    // Escape, read past the text gate. It is the rebind listener's cancel and the dashboard's toggle,
    // so it has to answer even while a field is eating keystrokes.
    public bool CaptureEscape() => _input?.Keyboard?.IsKeyJustPressed(Key.Escape) == true;

    public bool TryCaptureControl(out ControlRef control)
    {
        control = default;
        var keyboard = _input?.Keyboard;
        if (keyboard == null)
            return false;

        foreach (var key in InputControlCatalog.Keys)
        {
            // Escape is the listener's cancel; it can never be captured as a binding.
            if (key == Key.Escape)
                continue;
            if (keyboard.IsKeyJustPressed(key))
            {
                control = ControlRef.Key(key);
                return true;
            }
        }
        return false;
    }
}

public sealed class MouseSource : IInputSource
{
    private readonly InputInterface _input;

    public InputDeviceKind Kind => InputDeviceKind.Mouse;
    public bool IsAvailable => _input?.Mouse != null;

    public MouseSource(InputInterface input)
    {
        _input = input;
    }

    public bool ReadDigital(in ControlRef control)
    {
        var mouse = _input?.Mouse;
        if (mouse == null)
            return false;
        return control.Control switch
        {
            InputControlCatalog.MouseLeft => mouse.LeftButton.Held,
            InputControlCatalog.MouseRight => mouse.RightButton.Held,
            InputControlCatalog.MouseMiddle => mouse.MiddleButton.Held,
            InputControlCatalog.MouseButton4 => mouse.MouseButton4.Held,
            InputControlCatalog.MouseButton5 => mouse.MouseButton5.Held,
            _ => false
        };
    }

    public float ReadAnalog(in ControlRef control)
    {
        var mouse = _input?.Mouse;
        if (mouse == null)
            return 0f;
        if (control.Control == InputControlCatalog.MouseWheel)
            return mouse.ScrollWheelDelta.Value;
        return ReadDigital(control) ? 1f : 0f;
    }

    public float2 ReadAnalog2D(in ControlRef control)
    {
        var mouse = _input?.Mouse;
        if (mouse == null)
            return float2.Zero;
        if (control.Control == InputControlCatalog.MouseDelta)
            return mouse.DirectDelta.Value;
        return float2.Zero;
    }

    public bool TryCaptureControl(out ControlRef control)
    {
        control = default;
        var mouse = _input?.Mouse;
        if (mouse == null)
            return false;

        if (mouse.LeftButton.Pressed) { control = ControlRef.Mouse(InputControlCatalog.MouseLeft); return true; }
        if (mouse.RightButton.Pressed) { control = ControlRef.Mouse(InputControlCatalog.MouseRight); return true; }
        if (mouse.MiddleButton.Pressed) { control = ControlRef.Mouse(InputControlCatalog.MouseMiddle); return true; }
        if (mouse.MouseButton4.Pressed) { control = ControlRef.Mouse(InputControlCatalog.MouseButton4); return true; }
        if (mouse.MouseButton5.Pressed) { control = ControlRef.Mouse(InputControlCatalog.MouseButton5); return true; }
        if (MathF.Abs(mouse.ScrollWheelDelta.Value) > 0.01f) { control = ControlRef.Mouse(InputControlCatalog.MouseWheel); return true; }
        return false;
    }
}

// Two of these exist, one per hand, and bindings pick by chirality.
public sealed class VRControllerSource : IInputSource
{
    private readonly InputInterface _input;
    private readonly Chirality _side;

    public InputDeviceKind Kind => InputDeviceKind.VRController;
    public Chirality Side => _side;

    private VRController? Controller => _side == Chirality.Left ? _input?.LeftController : _input?.RightController;

    public bool IsAvailable => Controller is { IsDeviceActive: true };

    public VRControllerSource(InputInterface input, Chirality side)
    {
        _input = input;
        _side = side;
    }

    public bool ReadDigital(in ControlRef control)
    {
        var controller = Controller;
        if (controller == null)
            return false;
        return control.Control switch
        {
            InputControlCatalog.VRTrigger => controller.TriggerPressed,
            // Runtimes differ on whether grip reports a boolean, an axis, or both. Take either so a
            // squeeze is a squeeze on every headset.
            InputControlCatalog.VRGrip => controller.GripPressed || controller.GripValue > 0.5f,
            InputControlCatalog.VRPrimary => controller.PrimaryButtonPressed,
            InputControlCatalog.VRSecondary => controller.SecondaryButtonPressed,
            InputControlCatalog.VRMenu => controller.MenuButtonPressed,
            InputControlCatalog.VRStickPress => controller.ThumbstickPressed,
            InputControlCatalog.VRStickTouch => controller.ThumbstickTouched,
            _ => false
        };
    }

    public float ReadAnalog(in ControlRef control)
    {
        var controller = Controller;
        if (controller == null)
            return 0f;
        return control.Control switch
        {
            InputControlCatalog.VRTrigger => controller.TriggerValue,
            InputControlCatalog.VRGrip => controller.GripValue,
            _ => ReadDigital(control) ? 1f : 0f
        };
    }

    public float2 ReadAnalog2D(in ControlRef control)
    {
        var controller = Controller;
        if (controller == null || control.Control != InputControlCatalog.VRStick)
            return float2.Zero;
        var stick = controller.ThumbstickPosition;
        return new float2(stick.X, stick.Y);
    }

    public bool TryCaptureControl(out ControlRef control)
    {
        control = default;
        var controller = Controller;
        if (controller == null || !controller.IsDeviceActive)
            return false;

        foreach (var id in InputControlCatalog.ControlsFor(InputDeviceKind.VRController))
        {
            if (id == InputControlCatalog.VRStick)
                continue;
            var candidate = ControlRef.VR(_side, id);
            if (ReadDigital(candidate))
            {
                control = candidate;
                return true;
            }
        }

        var stickValue = controller.ThumbstickPosition;
        if (stickValue.X * stickValue.X + stickValue.Y * stickValue.Y > 0.64f)
        {
            control = ControlRef.VR(_side, InputControlCatalog.VRStick);
            return true;
        }
        return false;
    }
}

// Absent until one is plugged in, at which point its bindings wake up.
public sealed class GamepadSource : IInputSource
{
    private readonly InputInterface _input;

    // Stick deflection a rebind listener needs before it counts a stick as "the input".
    private const float CaptureThreshold = 0.7f;

    public InputDeviceKind Kind => InputDeviceKind.Gamepad;

    private Gamepad? Pad => _input?.Gamepad;
    public bool IsAvailable => Pad is { IsConnected: true };

    public GamepadSource(InputInterface input)
    {
        _input = input;
    }

    public bool ReadDigital(in ControlRef control)
    {
        var pad = Pad;
        if (pad == null || !pad.IsConnected)
            return false;
        return pad.GetButton(control.Control)?.Held == true;
    }

    public float ReadAnalog(in ControlRef control)
    {
        var pad = Pad;
        if (pad == null || !pad.IsConnected)
            return 0f;
        return control.Control switch
        {
            InputControlCatalog.PadLeftTrigger => pad.LeftTrigger.Value,
            InputControlCatalog.PadRightTrigger => pad.RightTrigger.Value,
            _ => ReadDigital(control) ? 1f : 0f
        };
    }

    public float2 ReadAnalog2D(in ControlRef control)
    {
        var pad = Pad;
        if (pad == null || !pad.IsConnected)
            return float2.Zero;
        return control.Control switch
        {
            InputControlCatalog.PadLeftStick => pad.LeftStick.Value,
            InputControlCatalog.PadRightStick => pad.RightStick.Value,
            _ => float2.Zero
        };
    }

    public bool TryCaptureControl(out ControlRef control)
    {
        control = default;
        var pad = Pad;
        if (pad == null || !pad.IsConnected)
            return false;

        foreach (var id in InputControlCatalog.ControlsFor(InputDeviceKind.Gamepad))
        {
            if (id is InputControlCatalog.PadLeftStick or InputControlCatalog.PadRightStick)
                continue;
            var button = pad.GetButton(id);
            if (button != null && button.Pressed)
            {
                control = ControlRef.Pad(id);
                return true;
            }
        }

        if (pad.LeftTrigger.Value > CaptureThreshold) { control = ControlRef.Pad(InputControlCatalog.PadLeftTrigger); return true; }
        if (pad.RightTrigger.Value > CaptureThreshold) { control = ControlRef.Pad(InputControlCatalog.PadRightTrigger); return true; }
        if (pad.LeftStick.Value.Length > CaptureThreshold) { control = ControlRef.Pad(InputControlCatalog.PadLeftStick); return true; }
        if (pad.RightStick.Value.Length > CaptureThreshold) { control = ControlRef.Pad(InputControlCatalog.PadRightStick); return true; }
        return false;
    }
}
