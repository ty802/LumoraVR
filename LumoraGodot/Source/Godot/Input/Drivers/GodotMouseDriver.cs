// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;
using Godot;
using Lumora.Source.UI;
using Lumora.Source.Godot.UI;

namespace Lumora.Source.Godot.Input.Drivers;

public class GodotMouseDriver : IMouseDriver, IInputDriver
{
    public bool TouchEnabled { get; set; }
    private Vector2 _touchPosition;
    private Vector2 _touchMotion;
    private bool _touchPrimary;

    public void AddTouchMotion(Vector2 motion) => _touchMotion += motion;
    public void SetTouchPointer(Vector2 position, bool pressed)
    {
        _touchPosition = position;
        _touchPrimary = pressed;
    }
    public void ClearTouchInput()
    {
        _touchMotion = Vector2.Zero;
        _touchPrimary = false;
        _smoothedDelta = float2.Zero;
    }

    public int UpdateOrder => 0;
    private float _pendingScrollDelta = 0f;
    private float _pendingHorizontalScrollDelta = 0f;
    private double _lastFrameTime = 0;
    private const float LookReferenceHeight = 1080f;

    // Accumulated motion - only cleared when consumed
    private Vector2 _pendingMotion = Vector2.Zero;
    private global::Godot.Input.MouseModeEnum _lastAppliedMouseMode = global::Godot.Input.MouseModeEnum.Visible;

    // WHEN THE GAME MAY TAKE THE MOUSE.
    //
    // Setting the captured mode clips the cursor to our window and warps it to the middle whether or not the
    // window has focus, and the platform takes it again by itself the moment focus returns. So a world
    // finishing its load while the player sat in another app dragged their cursor back into the game, and a
    // click on our title bar captured before the drag could start, so the window could not be moved off.
    // Now an unfocused window never holds the mouse, and once focus has been away (or at startup) the game
    // waits for a click inside its own view before taking it again. That click is swallowed so it does not
    // grab or press whatever the cursor was over. -xlinka
    private bool _waitForClick = true;
    private bool _swallowClick;
    private bool _anyButtonBefore;

    // Reject impossible deltas from recapture/focus glitches while preserving normal fast flicks.
    // Threshold is in pixels (before normalization).
    private const float MaxReasonableMotionPerFrame = 2500f;
    private const float MaxReasonableMotionSq = MaxReasonableMotionPerFrame * MaxReasonableMotionPerFrame;

    // Smoothing factor is read from InterfaceSettings.MouseSmoothing each frame so
    // the settings slider takes effect immediately. 0 = raw input (recommended at
    // high refresh rates).
    private float2 _smoothedDelta = float2.Zero;

    public GodotMouseDriver()
    {
        // Favor lower-latency raw events; we explicitly accumulate in HandleInputEvent.
        global::Godot.Input.UseAccumulatedInput = false;
    }

    public void UpdateMouse(Mouse mouse)
    {
        if (mouse == null)
            return;

        bool focused = DisplayServer.WindowIsFocused();
        bool anyButton = global::Godot.Input.IsMouseButtonPressed(MouseButton.Left)
            || global::Godot.Input.IsMouseButtonPressed(MouseButton.Right)
            || global::Godot.Input.IsMouseButtonPressed(MouseButton.Middle);
        bool clicked = anyButton && !_anyButtonBefore;
        _anyButtonBefore = anyButton;
        if (!focused)
            _waitForClick = true;

        bool captureWanted = Lumora.Core.Components.UserInputState.FocusedMouseCaptureRequested;
        var desiredMouseMode = global::Godot.Input.MouseModeEnum.Visible;
        if (!focused || TouchEnabled)
        {
            desiredMouseMode = global::Godot.Input.MouseModeEnum.Visible;
        }
        // Dashboard takes priority - hide system cursor but allow mouse movement (we use custom cursor)
        else if (DashboardToggle.IsDashboardVisible)
        {
            desiredMouseMode = global::Godot.Input.MouseModeEnum.Hidden;
            // Someone using the open dash in a focused window is already here, so closing it is the way back
            // in and takes the mouse at once, with no extra click.
            _waitForClick = false;
        }
        else if (captureWanted)
        {
            if (_waitForClick && clicked)
            {
                _waitForClick = false;
                _swallowClick = true;
            }
            if (!_waitForClick)
                desiredMouseMode = global::Godot.Input.MouseModeEnum.Captured;
        }
        if (_swallowClick && !anyButton)
            _swallowClick = false;
        bool withheld = !focused || (captureWanted && _waitForClick && !DashboardToggle.IsDashboardVisible);

        if (global::Godot.Input.MouseMode != desiredMouseMode)
        {
            global::Godot.Input.MouseMode = desiredMouseMode;
        }

        if (_lastAppliedMouseMode != desiredMouseMode)
        {
            // Drop stale motion on capture/visibility transitions to avoid one-frame jumps.
            _pendingMotion = Vector2.Zero;
            _smoothedDelta = float2.Zero;
            _lastAppliedMouseMode = desiredMouseMode;
        }

        // Calculate delta time manually since we can't access Engine's delta
        double currentTime = Time.GetTicksMsec() / 1000.0;
        float deltaTime = _lastFrameTime > 0 ? (float)(currentTime - _lastFrameTime) : 0.016f;
        _lastFrameTime = currentTime;

        mouse.LeftButton.UpdateState(!_swallowClick && (global::Godot.Input.IsMouseButtonPressed(MouseButton.Left) || (TouchEnabled && _touchPrimary)));
        mouse.RightButton.UpdateState(!_swallowClick && global::Godot.Input.IsMouseButtonPressed(MouseButton.Right));
        mouse.MiddleButton.UpdateState(!_swallowClick && global::Godot.Input.IsMouseButtonPressed(MouseButton.Middle));
        mouse.MouseButton4.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Xbutton1));
        mouse.MouseButton5.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Xbutton2));

        Vector2 mousePos = TouchEnabled ? _touchPosition : DisplayServer.MouseGetPosition();
        float2 position = new float2(mousePos.X, mousePos.Y);

        mouse.DesktopPosition.UpdateValue(position, deltaTime);
        mouse.Position.UpdateValue(position, deltaTime);

        // Pull raw motion gathered from _Input (in pixels).
        Vector2 motion = _pendingMotion + (TouchEnabled ? _touchMotion : Vector2.Zero);
        _pendingMotion = Vector2.Zero;
        _touchMotion = Vector2.Zero;

        // Clamp impossible spikes (alt-tab, recapture glitches)
        if (motion.LengthSquared() > MaxReasonableMotionSq)
        {
            motion = motion.Normalized() * MaxReasonableMotionPerFrame;
        }

        float sensitivity = Lumora.Core.EngineSettings.MouseSensitivity;
        float2 normalizedDelta = new float2(
            motion.X / LookReferenceHeight,
            motion.Y / LookReferenceHeight) * sensitivity;

        float smoothFactor = Lumora.Core.EngineSettings.MouseSmoothing;
        if (smoothFactor > 0f)
        {
            _smoothedDelta = float2.Lerp(_smoothedDelta, normalizedDelta, 1f - smoothFactor);
        }
        else
        {
            _smoothedDelta = normalizedDelta;
        }

        // A free cursor moving over the window, or one waiting for its click back in, turns nothing.
        float2 delta = DashboardToggle.IsDashboardVisible || withheld ? float2.Zero : _smoothedDelta;

        mouse.DirectDelta.UpdateValue(delta, deltaTime);

        float scrollDelta = _pendingScrollDelta;
        _pendingScrollDelta = 0f;
        mouse.ScrollWheelDelta.UpdateValue(scrollDelta, deltaTime);

        // Tilting the wheel, or a sideways trackpad swipe: right is positive.
        float horizontalDelta = _pendingHorizontalScrollDelta;
        _pendingHorizontalScrollDelta = 0f;
        mouse.HorizontalScrollDelta.UpdateValue(horizontalDelta, deltaTime);
    }

    // Fed from _Input; UpdateMouse drains what piles up here.
    public void HandleInputEvent(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            _pendingMotion += mouseMotion.Relative;
        }
        else if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _pendingScrollDelta += 1f;
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _pendingScrollDelta -= 1f;
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelRight)
            {
                _pendingHorizontalScrollDelta += 1f;
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelLeft)
            {
                _pendingHorizontalScrollDelta -= 1f;
            }
        }
    }

    public void RegisterInputs(InputInterface inputInterface)
    {
        // Mouse driver doesn't need to register additional inputs
        // Mouse device is created by InputInterface
    }

    public void UpdateInputs(float deltaTime)
    {
        // Update mouse via UpdateMouse() called by InputInterface
    }
}
