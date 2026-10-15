// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
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
    public bool TextFocusHeld
    {
        get => _textFocusHeld;
        set
        {
            if (_textFocusHeld && !value)
            {
                var keyboard = _input?.Keyboard;
                if (keyboard != null)
                {
                    foreach (var key in InputControlCatalog.Keys)
                    {
                        if (keyboard.IsKeyPressed(key))
                            _downFromText.Add(key);
                    }
                }
            }
            _textFocusHeld = value;
        }
    }

    private bool _textFocusHeld;

    // Keys still down when the field let go. The Escape that left a field, or the last letter typed into
    // it, has to come up before it drives anything; read straight away it lands as a fresh press the
    // frame the gate lifts, and Escape opened the dash as it closed the field. -xlinka
    private readonly System.Collections.Generic.HashSet<Key> _downFromText = new();

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
        if (!InputControlCatalog.TryParseKey(control.Control, out var key))
            return false;
        bool down = keyboard.IsKeyPressed(key);
        if (_downFromText.Count > 0 && _downFromText.Contains(key))
        {
            if (down)
                return false;
            _downFromText.Remove(key);
        }
        return down;
    }

    public float ReadAnalog(in ControlRef control) => ReadDigital(control) ? 1f : 0f;
    public float2 ReadAnalog2D(in ControlRef control) => float2.Zero;

    // Escape, read past the text gate. It is the rebind listener's cancel and the dashboard's toggle,
    // so it has to answer even while a field is eating keystrokes.
    public bool CaptureEscape() => _input?.Keyboard?.IsKeyJustPressed(Key.Escape) == true;

    private static bool IsModifier(Key key) => key is Key.LeftControl or Key.RightControl
        or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt;

    private static readonly Key[] ChordModifiers =
    {
        Key.LeftControl, Key.RightControl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift
    };

    // Snapshot the whole chord while its keys are down. Keep the letter last even when a modifier
    // was pressed after it; the snapshot survives release for both capture and the last-input readout.
    public InputBinding CaptureChord(ControlRef primary)
    {
        var keyboard = _input?.Keyboard;
        if (keyboard == null || primary.Device is not (InputDeviceKind.Keyboard or InputDeviceKind.Mouse))
            return new InputBinding(primary);
        if (primary.Device == InputDeviceKind.Keyboard && InputControlCatalog.TryParseKey(primary.Control, out var key) && IsModifier(key))
        {
            foreach (var held in InputControlCatalog.Keys)
                if (held != Key.Escape && !IsModifier(held) && keyboard.IsKeyPressed(held))
                    primary = ControlRef.Key(held);
        }
        var modifiers = new List<ControlRef>();
        foreach (var held in ChordModifiers)
            Add(held);
        foreach (var held in InputControlCatalog.Keys)
            if (held != Key.Escape && !IsModifier(held))
                Add(held);
        return new InputBinding(primary, modifiers: modifiers);

        void Add(Key held)
        {
            var control = ControlRef.Key(held);
            if (control != primary && keyboard.IsKeyPressed(held))
                modifiers.Add(control);
        }
    }

    public bool ChordIsHeld(InputBinding chord)
    {
        if (!Held(chord.Control))
            return false;
        foreach (var modifier in chord.Modifiers)
            if (!Held(modifier))
                return false;
        return true;
    }

    // Between movement directions, let Ctrl/Shift/Alt stay held but require the direction keys to
    // come up. Otherwise a previous direction can accidentally become part of the next chord.
    public bool ChordKeysStillHeld(InputBinding chord)
    {
        if (HeldNonModifier(chord.Control))
            return true;
        foreach (var modifier in chord.Modifiers)
            if (HeldNonModifier(modifier))
                return true;
        return false;
    }

    private bool Held(in ControlRef control) => control.Device == InputDeviceKind.Keyboard
        && InputControlCatalog.TryParseKey(control.Control, out var key) && _input?.Keyboard?.IsKeyPressed(key) == true;

    private bool HeldNonModifier(in ControlRef control) => Held(control)
        && InputControlCatalog.TryParseKey(control.Control, out var key) && !IsModifier(key);

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
            // The latch only. The analog grip is finger curl and reads above half from a relaxed
            // hold on a force-sensing handle, so it can never stand in for a press. -xlinka
            InputControlCatalog.VRGrip => controller.GripPressed,
            InputControlCatalog.VRPrimary => controller.PrimaryButtonPressed,
            InputControlCatalog.VRSecondary => controller.SecondaryButtonPressed,
            InputControlCatalog.VRFaceLeft => controller.FaceLeftPressed,
            InputControlCatalog.VRFaceUp => controller.FaceUpPressed,
            InputControlCatalog.VRShoulder => controller.ShoulderPressed,
            InputControlCatalog.VRMenu => controller.MenuButtonPressed,
            InputControlCatalog.VRStickPress => controller.ThumbstickPressed,
            InputControlCatalog.VRStickTouch => controller.ThumbstickTouched,
            InputControlCatalog.VRPadPress => controller.TouchpadPressed,
            InputControlCatalog.VRSelectPress => ReadSelectPress(controller),
            InputControlCatalog.VRMenuHold => controller.MenuHoldFired,
            InputControlCatalog.VRMenuTap => controller.MenuTapFired,
            _ => false
        };
    }

    private bool ReadSelectPress(VRController controller)
        => SelectionUsesTouchpad(_input, _side) ? controller.TouchpadPressed : controller.ThumbstickPressed;

    internal static bool SelectionUsesTouchpad(InputInterface input, Chirality side)
    {
        var driver = input.ActiveVRDriver;
        var profile = side == Chirality.Left ? driver?.LeftInteractionProfile : driver?.RightInteractionProfile;
        // Index has both controls: selection belongs to the pad, leaving its stick click available.
        return profile?.Contains("/valve/index_controller", StringComparison.OrdinalIgnoreCase) == true;
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

    // The trigger's deep level and its one-frame edges, for an action bound to the trigger.
    public (bool Held, bool Pressed, bool Released) ReadTriggerDepth()
    {
        var controller = Controller;
        if (controller == null)
            return default;
        return (controller.TriggerDeep, controller.TriggerDeepPressed, controller.TriggerDeepReleased);
    }

    public float2 ReadAnalog2D(in ControlRef control)
    {
        var controller = Controller;
        if (controller == null)
            return float2.Zero;
        switch (control.Control)
        {
            case InputControlCatalog.VRStick:
                var stick = controller.ThumbstickPosition;
                return new float2(stick.X, stick.Y);
            case InputControlCatalog.VRPad:
                var pad = controller.TouchpadPosition;
                return new float2(pad.X, pad.Y);
            default:
                return float2.Zero;
        }
    }

    public bool TryCaptureControl(out ControlRef control)
    {
        control = default;
        var controller = Controller;
        if (controller == null || !controller.IsDeviceActive)
            return false;

        foreach (var id in InputControlCatalog.ControlsFor(InputDeviceKind.VRController))
        {
            if (id is InputControlCatalog.VRStick or InputControlCatalog.VRPad)
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
        var padValue = controller.TouchpadPosition;
        if (padValue.X * padValue.X + padValue.Y * padValue.Y > 0.64f)
        {
            control = ControlRef.VR(_side, InputControlCatalog.VRPad);
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
