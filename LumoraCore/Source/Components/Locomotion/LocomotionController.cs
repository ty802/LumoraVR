// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Input;
using LumoraLogger = Lumora.Core.Logging.Logger;
using EngineKey = Lumora.Core.Input.Key;

namespace Lumora.Core.Components;

/// <summary>
/// Manages user movement via input drivers and CharacterController.
/// Reads input and calculates movement direction.
/// </summary>
[ComponentCategory("Users")]
public class LocomotionController : Component
{
    // ===== PARAMETERS =====

    // DirectDelta is now normalized (1.0 = full screen height swipe).
    // Sensitivity of Pi means a full screen-height swipe = 180 degrees.
    public float MouseSensitivity { get; set; } = MathF.PI;
    public float MaxPitch { get; set; } = 89.0f;

    // ===== STATE =====

    private CharacterController _characterController = null!;
    private UserRoot _userRoot = null!;
    private UserInputState _inputState = null!;
    private Mouse _mouse = null!;
    private IKeyboardDriver _keyboardDriver = null!;
    private InputInterface _inputInterface = null!;
    private readonly System.Collections.Generic.List<LocomotionModule> _modules = new();
    private LocomotionModule _activeModule = null!;
    private bool _nextModuleHeld;
    private bool _prevModuleHeld;
    private readonly LocomotionPermissions _permissions = new LocomotionPermissions();

    private float _pitch = 0.0f;
    private float _yaw = 0.0f;
    private bool _mouseCaptured = false;
    private bool _escapeWasPressed = false;
    private bool _initialized = false;
    private bool _loggedMissingUserRoot = false;
    private bool _loggedActiveUserState = false;

    public UserInputState InputState => _inputState;
    public LocomotionModule ActiveModule => _activeModule;
    public System.Collections.Generic.IReadOnlyList<LocomotionModule> Modules => _modules;

    public void RegisterModule(LocomotionModule module)
    {
        if (module == null || _modules.Contains(module))
            return;
        _modules.Add(module);
        SortModules();
    }

    public void UnregisterModule(LocomotionModule module)
    {
        if (module == null) return;
        if (_activeModule == module)
        {
            _activeModule.DeactivateInternal();
            _activeModule = null!;
        }
        _modules.Remove(module);
    }

    private void SortModules()
    {
        _modules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    // ===== INITIALIZATION =====

    public override void OnAwake()
    {
        base.OnAwake();

        LumoraLogger.Log($"LocomotionController: OnAwake started");
        TryInitializeLocalUser();
    }

    private void CaptureMouse()
    {
        _mouseCaptured = true;
        _inputState?.SetMouseCaptureRequested(true);
    }

    // ===== UPDATE =====

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Late-bind InputInterface if it was not ready during Awake
        if (_inputInterface == null && Lumora.Core.Engine.Current?.InputInterface != null)
        {
            _inputInterface = Lumora.Core.Engine.Current.InputInterface;
            _mouse = _inputInterface.Mouse;
            _keyboardDriver = _inputInterface.GetKeyboardDriver();
        }

        if (!TryInitializeLocalUser())
            return;

        if (_characterController == null || !(_userRoot?.IsLocalUserRoot ?? false))
            return;

        EnsurePlatformModule();
        HandleModuleSwitching();

        // Always handle mouse look (updates head rotation)
        HandleMouseLook(delta);

        // Apply head/body rotation before movement so basis uses latest look
        UpdateHead();

        _activeModule?.OnModuleUpdate(delta);
    }

    private bool TryInitializeLocalUser()
    {
        if (_initialized)
            return true;

        if (_userRoot == null)
        {
            _userRoot = Slot.GetComponent<UserRoot>();
            if (_userRoot == null)
            {
                if (!_loggedMissingUserRoot)
                {
                    LumoraLogger.Warn("LocomotionController: No UserRoot found!");
                    _loggedMissingUserRoot = true;
                }
                return false;
            }
        }

        if (!_loggedActiveUserState)
        {
            LumoraLogger.Log($"LocomotionController: UserRoot.ActiveUser={_userRoot.ActiveUser?.UserName?.Value ?? "(null)"}, World.LocalUser={World?.LocalUser?.UserName?.Value ?? "(null)"}");
            _loggedActiveUserState = true;
        }

        if (!_userRoot.IsLocalUserRoot)
            return false;

        _characterController = Slot.GetComponent<CharacterController>();
        if (_characterController == null)
        {
            LumoraLogger.Warn("LocomotionController: No CharacterController found!");
            return false;
        }

        _inputState = Slot.GetComponent<UserInputState>() ?? Slot.AttachComponent<UserInputState>();

        _inputInterface = Lumora.Core.Engine.Current?.InputInterface!;
        if (_inputInterface != null)
        {
            _mouse = _inputInterface.Mouse;
            _keyboardDriver = _inputInterface.GetKeyboardDriver();
            LumoraLogger.Log($"LocomotionController: Input bound - Mouse={_mouse != null}, Keyboard={_keyboardDriver != null}");
        }
        else
        {
            LumoraLogger.Warn("[LocomotionController] No InputInterface found - will try late binding");
        }

        EnsureDefaultModules();
        ActivateBestModule();

        if (!IsVRActive())
            _inputState.SetMouseCaptureRequested(true);

        LumoraLogger.Log($"LocomotionController: Initialized for user '{_userRoot.ActiveUser?.UserName?.Value}' (VR={IsVRActive()}, Module={_activeModule?.DisplayName})");
        _initialized = true;
        return true;
    }

    // Make sure baseline modules are attached, then pull in anything else
    // already on the slot. PhysicalLocomotion is one module that handles both
    // stick and WASD via LocomotionInputHelper, so we no longer attach a
    // separate VR module or desktop module. - xlinka
    private void EnsureDefaultModules()
    {
        Slot.GetOrAttachComponent<PhysicalLocomotion>();
        Slot.GetOrAttachComponent<NullLocomotionModule>();

        _modules.Clear();
        foreach (var m in Slot.GetComponents<LocomotionModule>())
            _modules.Add(m);
        SortModules();
    }

    private LocomotionModule PickBestEligibleModule()
    {
        for (int i = 0; i < _modules.Count; i++)
        {
            if (_modules[i].CanActivate())
                return _modules[i];
        }
        return null!;
    }

    private void ActivateBestModule()
    {
        var pick = PickBestEligibleModule();
        if (pick != null && pick != _activeModule)
            ActivateModule(pick);
    }

    private void HandleModuleSwitching()
    {
        if (_keyboardDriver == null || _modules.Count <= 1)
            return;
        if ((_inputState?.DesktopInputSuppressed ?? false) || _mouse?.RightButton.Held == true)
        {
            _nextModuleHeld = _keyboardDriver.GetKeyState(EngineKey.E);
            _prevModuleHeld = _keyboardDriver.GetKeyState(EngineKey.Q);
            return;
        }

        bool nextDown = _keyboardDriver.GetKeyState(EngineKey.E);
        bool prevDown = _keyboardDriver.GetKeyState(EngineKey.Q);

        if (nextDown && !_nextModuleHeld) CycleModule(+1);
        if (prevDown && !_prevModuleHeld) CycleModule(-1);

        _nextModuleHeld = nextDown;
        _prevModuleHeld = prevDown;
    }

    // Walk through modules from the current position, skipping anything whose
    // CanActivate returns false. If nothing else is eligible, stay put.
    private void CycleModule(int direction)
    {
        if (_modules.Count == 0) return;

        int currentIdx = _activeModule != null ? _modules.IndexOf(_activeModule) : -1;
        for (int step = 1; step <= _modules.Count; step++)
        {
            int idx = currentIdx + direction * step;
            if (idx < 0) idx += _modules.Count * ((-idx / _modules.Count) + 1);
            idx %= _modules.Count;
            var candidate = _modules[idx];
            if (candidate != _activeModule && candidate.CanActivate())
            {
                ActivateModule(candidate);
                return;
            }
        }
    }

    // Re-evaluates eligibility each tick: if VR powered up or dropped out
    // mid-session, we re-pick the best module. Cheap, three modules.
    private void EnsurePlatformModule()
    {
        if (_activeModule == null || !_activeModule.CanActivate())
        {
            ActivateBestModule();
        }
        else
        {
            var best = PickBestEligibleModule();
            if (best != null && best.Priority > _activeModule.Priority)
                ActivateModule(best);
        }

        if (!IsVRActive() && !_mouseCaptured)
            CaptureMouse();
    }

    private bool IsVRActive()
    {
        if (_inputInterface == null)
            return false;

        // VR considered active when explicitly flagged and controllers/head tracked
        bool controllersTracked = _inputInterface.LeftController?.IsTracked == true || _inputInterface.RightController?.IsTracked == true;
        bool headTracked = _inputInterface.HeadDevice?.IsTracked == true;
        return _inputInterface.VR_Active && (controllersTracked || headTracked);
    }

    private void HandleMouseLook(float delta)
    {
        // Check for Escape to toggle mouse capture (only on key press, not hold)
        bool escapePressed = _keyboardDriver != null && _keyboardDriver.GetKeyState(EngineKey.Escape);
        if (escapePressed && !_escapeWasPressed)
        {
            _mouseCaptured = !_mouseCaptured;
            _inputState?.SetMouseCaptureRequested(_mouseCaptured);
            LumoraLogger.Log($"[LocomotionController] Mouse capture toggled: {_mouseCaptured}");
        }
        _escapeWasPressed = escapePressed;

        bool freeCam = _inputState?.FreeCamActive ?? false;
        bool lookSuppressed = _inputState?.MouseLookSuppressed ?? false;
        bool inputSuppressed = _inputState?.DesktopInputSuppressed ?? false;
        if (_mouse == null || !_mouseCaptured || freeCam || lookSuppressed || inputSuppressed)
            return;

        // Use Mouse.DirectDelta - now populated via GodotMouseDriver.HandleInputEvent
        float2 mouseDelta = _mouse.DirectDelta.Value;

        // Update yaw/pitch
        _yaw -= mouseDelta.x * MouseSensitivity;
        _pitch -= mouseDelta.y * MouseSensitivity;
        float maxPitchRad = MaxPitch * (float)System.Math.PI / 180f;
        _pitch = System.Math.Clamp(_pitch, -maxPitchRad, maxPitchRad);
    }

    private const float POS_THRESHOLD_SQ = 0.0001f * 0.0001f;
    private const float ROT_THRESHOLD = 0.0001f;

    private void UpdateHead()
    {
        if (_userRoot?.HeadSlot == null)
            return;

        bool headTracked = _inputInterface?.HeadDevice?.IsTracked == true;
        if (headTracked)
            return;

        float userHeight = _inputInterface?.UserHeight ?? InputInterface.DEFAULT_USER_HEIGHT;
        float headHeight = userHeight - InputInterface.EYE_HEAD_OFFSET;
        var newHeadPos = new float3(0, headHeight, 0);

        var currentHeadPos = _userRoot.HeadSlot.LocalPosition.Value;
        if ((newHeadPos - currentHeadPos).LengthSquared > POS_THRESHOLD_SQ)
            _userRoot.HeadSlot.LocalPosition.Value = newHeadPos;

        var newBodyRot = floatQ.FromEuler(new float3(0, _yaw, 0));
        var currentBodyRot = Slot.GlobalRotation;
        float bodyDot = floatQ.Dot(newBodyRot, currentBodyRot);
        if (1.0f - (bodyDot < 0 ? -bodyDot : bodyDot) > ROT_THRESHOLD)
            Slot.GlobalRotation = newBodyRot;

        var newHeadRot = floatQ.FromEuler(new float3(_pitch, 0, 0));
        var currentHeadRot = _userRoot.HeadSlot.LocalRotation.Value;
        float headDot = floatQ.Dot(newHeadRot, currentHeadRot);
        if (1.0f - (headDot < 0 ? -headDot : headDot) > ROT_THRESHOLD)
            _userRoot.HeadSlot.LocalRotation.Value = newHeadRot;

        // Drive hand aim: right hand tilts up when looking up, left stays at rest.
        // Rest pitch = -Ï€/2 (fingers down). Add camera pitch so hand aims forward when looking level.
        float restPitch = -MathF.PI / 2f;
        var rightHandSlot = _userRoot.RightHandSlot;
        if (rightHandSlot != null)
        {
            float aimPitch = MathF.Min(MathF.Max(restPitch + _pitch, -MathF.PI / 2f), 0f);
            rightHandSlot.LocalRotation.Value = floatQ.Euler(-MathF.PI / 2f, aimPitch, 0f);
        }
        if (_userRoot.LeftHandSlot is { } leftHandSlot)
            leftHandSlot.LocalRotation.Value = floatQ.Euler(MathF.PI / 2f, restPitch, 0f);
    }

    /// <summary>
    /// Apply a snap turn by modifying internal yaw (for VR snap turns).
    /// </summary>
    public void ApplySnapTurn(float deltaYaw)
    {
        _yaw += deltaYaw;

        if (_userRoot != null)
        {
            _userRoot.RotateYawAroundHead(deltaYaw);
            return;
        }

        Slot.GlobalRotation = (floatQ.AxisAngle(float3.Up, deltaYaw) * Slot.GlobalRotation).Normalized;
    }

    /// <summary>
    /// Compute horizontal movement basis using head tracking if available, otherwise fall back to yaw.
    /// Movement is always on the horizontal plane in the direction the head is facing.
    /// </summary>
    public void GetMovementBasis(out float3 forward, out float3 right)
    {
        if (_userRoot != null && _userRoot.HeadSlot != null)
        {
            forward = _userRoot.HeadFacingDirection;
        }
        else if (_inputInterface?.GetBodyNode(Input.BodyNode.Head) is ITrackedDevice headDevice && headDevice.IsTracking)
        {
            forward = headDevice.Rotation * float3.Backward;
        }
        else
        {
            // Desktop fallback: use yaw
            floatQ yawRotation = floatQ.AxisAngle(float3.Up, _yaw);
            forward = yawRotation * float3.Backward;
        }

        // Flatten to horizontal plane (remove vertical component)
        forward.y = 0;
        if (forward.LengthSquared < 1e-4f)
            forward = new float3(0, 0, -1); // Default forward is -Z
        forward = forward.Normalized;

        // Right as perpendicular on horizontal plane (Forward Ã— Up = Right in right-handed system)
        right = float3.Cross(forward, float3.Up);
        if (right.LengthSquared < 1e-4f)
            right = float3.Right;
        right = right.Normalized;
    }

    private void ActivateModule(LocomotionModule module)
    {
        if (module == null || !_permissions.CanUseLocomotion(module))
            return;

        _activeModule?.DeactivateInternal();
        _activeModule = module;
        _activeModule.ActivateInternal(this);
    }

    /// <summary>
    /// Expose CharacterController for modules.
    /// </summary>
    public CharacterController CharacterController => _characterController;

    // ===== CLEANUP =====

    public override void OnDestroy()
    {
        if (_mouseCaptured)
        {
            _inputState?.SetMouseCaptureRequested(false);
            _mouseCaptured = false;
        }

        _inputState?.SetDesktopInputSuppressed(this, false);

        base.OnDestroy();
    }
}

