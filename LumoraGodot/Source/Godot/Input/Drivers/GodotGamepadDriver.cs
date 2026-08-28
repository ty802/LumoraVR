// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Input.Drivers;

// Everything here goes through the SDL standardized button and axis names rather than raw indices,
// which is what makes "plug in whatever pad you own and it works" true: the platform ships the
// controller database, so an Xbox pad, a DualShock and a Switch pro pad all report FaceDown on the
// button that sits at the bottom of the diamond. Reading raw indices would have meant a per-vendor
// table that is wrong for the next pad released.
//
// Pads that have no database entry are IGNORED rather than guessed at. A pad with no mapping
// reports arbitrary indices, and a guessed layout that puts jump on the wrong button is worse than
// no pad support - it looks like the game is broken.
//
// One pad is active at a time, first-connected wins. Two people cannot share one avatar, and
// silently summing two pads' sticks turns a stuck controller in a drawer into an unexplainable
// drift bug. -xlinka
public class GodotGamepadDriver : IGamepadDriver, IInputDriver
{
    // After the drivers that fill the devices, before nothing in particular; the gamepad has no
    // ordering relationship with tracking.
    public int UpdateOrder => 0;

    // Stick hardware rests slightly off centre and wears looser over time. This is the floor every
    // pad axis has to clear before it counts as movement at all; per-action deadzones sit on top.
    private const float StickDeadzone = 0.12f;

    // A pull under this is finger weight on the trigger, not a pull.
    private const float TriggerFloor = 0.05f;

    private int _activeSlot = -1;
    private string _activeName = string.Empty;
    private int _connectedCount;
    private bool _subscribed;
    private readonly List<int> _connected = new();

    public string ActivePadName => _activeName;
    public int ConnectedPadCount => _connectedCount;

    public void RegisterInputs(InputInterface inputInterface)
    {
        // Hot-plug arrives as a signal so a pad connected mid-session is picked up on the frame it
        // appears rather than whenever something next happens to poll.
        if (_subscribed)
            return;
        try
        {
            global::Godot.Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
            _subscribed = true;
        }
        catch (Exception ex)
        {
            // Losing the signal only costs us the log line; RefreshConnected still runs every frame.
            LumoraLogger.Warn($"GodotGamepadDriver: hot-plug signal unavailable ({ex.Message}); falling back to polling.");
        }
        RefreshConnected();
    }

    public void UpdateInputs(float deltaTime)
    {
        // State is pulled in UpdateGamepad, which the input interface calls with the device.
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        RefreshConnected();
        if (connected)
        {
            string name = global::Godot.Input.GetJoyName((int)device);
            LumoraLogger.Log($"GodotGamepadDriver: pad connected in slot {device} ({name})");
        }
        else
            LumoraLogger.Log($"GodotGamepadDriver: pad disconnected from slot {device}");
    }

    private void RefreshConnected()
    {
        _connected.Clear();
        try
        {
            foreach (int slot in global::Godot.Input.GetConnectedJoypads())
            {
                // A pad with no database entry reports meaningless indices; see the type comment.
                if (!global::Godot.Input.IsJoyKnown(slot))
                    continue;
                _connected.Add(slot);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotGamepadDriver: could not enumerate joypads: {ex.Message}");
        }

        _connectedCount = _connected.Count;

        if (_activeSlot >= 0 && _connected.Contains(_activeSlot))
            return;

        _activeSlot = _connected.Count > 0 ? _connected[0] : -1;
        _activeName = _activeSlot >= 0 ? global::Godot.Input.GetJoyName(_activeSlot) : string.Empty;
        if (_activeSlot >= 0)
            LumoraLogger.Log($"GodotGamepadDriver: active pad is slot {_activeSlot} '{_activeName}' ({_connectedCount} connected)");
    }

    public void UpdateGamepad(Gamepad gamepad, float deltaTime)
    {
        if (gamepad == null)
            return;

        // Cheap enough to re-check every frame, and it means a pad that appeared without a signal
        // (a runtime that does not emit one, a device that re-enumerates) still comes online.
        RefreshConnected();

        if (_activeSlot < 0)
        {
            if (gamepad.IsConnected)
            {
                // Unplugging with a button down would otherwise leave that button held forever.
                gamepad.ClearState(deltaTime);
                gamepad.UpdateTriggerButtons();
                LumoraLogger.Log("GodotGamepadDriver: no pad connected");
            }
            gamepad.IsConnected = false;
            gamepad.IsDeviceActive = false;
            gamepad.DeviceName = "None";
            gamepad.SlotIndex = -1;
            return;
        }

        gamepad.IsConnected = true;
        gamepad.IsDeviceActive = true;
        gamepad.DeviceName = _activeName;
        gamepad.SlotIndex = _activeSlot;

        int slot = _activeSlot;

        gamepad.FaceDown.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.A));
        gamepad.FaceRight.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.B));
        gamepad.FaceLeft.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.X));
        gamepad.FaceUp.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.Y));
        gamepad.LeftShoulder.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.LeftShoulder));
        gamepad.RightShoulder.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.RightShoulder));
        gamepad.LeftStickPress.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.LeftStick));
        gamepad.RightStickPress.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.RightStick));
        gamepad.Start.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.Start));
        gamepad.Back.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.Back));
        gamepad.Guide.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.Guide));
        gamepad.DPadUp.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.DpadUp));
        gamepad.DPadDown.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.DpadDown));
        gamepad.DPadLeft.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.DpadLeft));
        gamepad.DPadRight.UpdateState(global::Godot.Input.IsJoyButtonPressed(slot, JoyButton.DpadRight));

        gamepad.LeftTrigger.UpdateValue(ReadTrigger(slot, JoyAxis.TriggerLeft), deltaTime);
        gamepad.RightTrigger.UpdateValue(ReadTrigger(slot, JoyAxis.TriggerRight), deltaTime);
        gamepad.UpdateTriggerButtons();

        gamepad.LeftStick.UpdateValue(ReadStick(slot, JoyAxis.LeftX, JoyAxis.LeftY), deltaTime);
        gamepad.RightStick.UpdateValue(ReadStick(slot, JoyAxis.RightX, JoyAxis.RightY), deltaTime);

        if (gamepad.TryConsumeRumble(out float weak, out float strong, out float duration))
        {
            try
            {
                if (weak <= 0f && strong <= 0f)
                    global::Godot.Input.StopJoyVibration(slot);
                else
                    global::Godot.Input.StartJoyVibration(slot, weak, strong, duration);
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"GodotGamepadDriver: rumble failed: {ex.Message}");
            }
        }
    }

    private static float ReadTrigger(int slot, JoyAxis axis)
    {
        float value = global::Godot.Input.GetJoyAxis(slot, axis);
        // Some pads rest triggers at -1 and pull to +1 instead of 0..1.
        if (value < 0f)
            value = (value + 1f) * 0.5f;
        return value <= TriggerFloor ? 0f : Mathf.Clamp(value, 0f, 1f);
    }

    private static float2 ReadStick(int slot, JoyAxis xAxis, JoyAxis yAxis)
    {
        float x = global::Godot.Input.GetJoyAxis(slot, xAxis);
        // Joypad Y runs screen-style (down is positive); the engine's axes run up-positive, and every
        // consumer of a stick already assumes forward is +Y.
        float y = -global::Godot.Input.GetJoyAxis(slot, yAxis);

        float magnitude = Mathf.Sqrt(x * x + y * y);
        if (magnitude <= StickDeadzone)
            return float2.Zero;

        // Rescale past the deadzone so the usable range still reaches full deflection instead of
        // topping out at 1 minus the deadzone.
        float scaled = Mathf.Min((magnitude - StickDeadzone) / (1f - StickDeadzone), 1f);
        float factor = scaled / magnitude;
        return new float2(x * factor, y * factor);
    }
}
