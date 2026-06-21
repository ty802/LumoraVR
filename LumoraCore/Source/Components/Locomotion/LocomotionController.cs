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
    // PARAMETERS

    // DirectDelta is now normalized (1.0 = full screen height swipe).
    // Sensitivity of Pi means a full screen-height swipe = 180 degrees.
    public float MouseSensitivity { get; set; } = MathF.PI;
    public float MaxPitch { get; set; } = 89.0f;

    // Whether the user allows being scaled (grab-scale gestures, world zoom).
    // The context menu exposes it as a toggle while at default scale.
    public readonly Sync<bool> ScalingEnabled = new();

    public override void OnInit()
    {
        base.OnInit();
        ScalingEnabled.Value = true;
    }

    // STATE

    private CharacterController _characterController = null!;
    private UserRoot _userRoot = null!;
    private UserInputState _inputState = null!;
    private Mouse _mouse = null!;
    private IKeyboardDriver _keyboardDriver = null!;
    private InputInterface _inputInterface = null!;
    private readonly System.Collections.Generic.List<LocomotionModule> _modules = new();
    private LocomotionModule _activeModule = null!;
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

    // Modules are kept in registration order (Walk, Noclip, ...); switching is
    // by list position, not priority (an ordered module list + active index).
    public void RegisterModule(LocomotionModule module)
    {
        if (module == null || _modules.Contains(module))
            return;
        _modules.Add(module);
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

    // INITIALIZATION

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

    // UPDATE

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

        // Everything below drives the LOCAL user's OWN avatar/root - head look, body turn, scale, and the
        // locomotion modules. That's engine movement of your own body, not a user edit, so bypass the data-model
        // permission gate: the root slot is host-allocated under the authority byte and ownership rests on the
        // User<->UserRoot link, which lags during join - without this the local writes are silently denied for a
        // window and you can't move/turn right after joining. -xlinka
        using var permBypass = World?.DataModelPermissions?.EnterSystemBypass();

        EnsurePlatformModule();

        // Always handle mouse look (updates head rotation)
        HandleMouseLook(delta);

        // Apply head/body rotation before movement so basis uses latest look
        UpdateHead();

        HandleDesktopScaling();

        _activeModule?.OnModuleUpdate(delta);
    }

    // Desktop user scaling: ctrl + scroll wheel. Crouch lives on C so ctrl
    // is free as the scale modifier.
    private const float MinUserScale = 0.05f;
    private const float MaxUserScale = 20f;

    private void HandleDesktopScaling()
    {
        if (_inputInterface == null || _inputInterface.IsVRActive)
            return;
        if (_inputInterface.IsDashboardOpen)
            return;
        if (!ScalingEnabled.Value)
            return;
        if (_keyboardDriver == null
            || (!_keyboardDriver.GetKeyState(EngineKey.LeftControl) && !_keyboardDriver.GetKeyState(EngineKey.RightControl)))
            return;

        float scroll = _mouse?.ScrollWheelDelta.Value ?? 0f;
        if (scroll == 0f)
            return;

        float factor = MathF.Pow(1.12f, scroll);
        _userRoot.GlobalScale = System.Math.Clamp(_userRoot.GlobalScale * factor, MinUserScale, MaxUserScale);
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
        ActivateDefaultModule();

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
        Slot.GetOrAttachComponent<NoclipLocomotion>();
        Slot.GetOrAttachComponent<NullLocomotionModule>();

        _modules.Clear();
        foreach (var m in Slot.GetComponents<LocomotionModule>())
            _modules.Add(m);
    }

    // First module in list order whose CanActivate passes (permission/eligibility).
    private LocomotionModule? FirstUsableModule()
    {
        for (int i = 0; i < _modules.Count; i++)
        {
            if (_modules[i].CanActivate())
                return _modules[i];
        }
        return null;
    }

    // Spawn in the default (first usable) module. Locomotion mode is session state,
    // not a persisted setting - the user opts into noclip/fly per session.
    private void ActivateDefaultModule()
    {
        var pick = FirstUsableModule();
        if (pick != null && pick != _activeModule)
            ActivateModule(pick);
    }

    // Keeps a usable module active: the selection is sticky, but if the current
    // module becomes ineligible (permission revoked, VR dropped) fall back to
    // the first usable one. Does not override an eligible user choice.
    private void EnsurePlatformModule()
    {
        // The active module is a sticky choice (menu selection / persisted),
        // never recomputed by priority. Only re-pick when the current module
        // becomes unusable (e.g. permission revoked), advancing to the first
        // usable one.
        if (_activeModule == null || !_activeModule.CanActivate())
        {
            var pick = FirstUsableModule();
            if (pick != null && pick != _activeModule)
                ActivateModule(pick);
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

    private TransformStreamDriver _rootStreamDriver = null!;

    // When a TransformStreamDriver shares the root slot, the Root stream is the transport for the root rotation,
    // so turn writes must be SILENT or the root would replicate over BOTH the stream and the delta channel.
    // Re-checks until found (the driver is attached during avatar build), then caches. -xlinka
    private bool RootIsStreamed()
    {
        if (_rootStreamDriver == null || _rootStreamDriver.IsDestroyed)
            _rootStreamDriver = Slot?.GetComponent<TransformStreamDriver>()!;
        return _rootStreamDriver != null && !_rootStreamDriver.IsDestroyed;
    }

    private void SetRootRotation(floatQ rotation)
    {
        if (RootIsStreamed())
            Slot.SetGlobalRotationSilently(rotation); // Root stream is the transport - don't also delta it.
        else
            Slot.GlobalRotation = rotation;
    }

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
            SetRootRotation(newBodyRot);

        var newHeadRot = floatQ.FromEuler(new float3(_pitch, 0, 0));
        var currentHeadRot = _userRoot.HeadSlot.LocalRotation.Value;
        float headDot = floatQ.Dot(newHeadRot, currentHeadRot);
        if (1.0f - (headDot < 0 ? -headDot : headDot) > ROT_THRESHOLD)
            _userRoot.HeadSlot.LocalRotation.Value = newHeadRot;

        // Drive hand aim on the CONTROLLER slots (hands are grip-offset
        // children of controllers — writing the hand child would bypass the
        // tool/laser). Right hand pitches with the camera; while the context
        // menu is open it raises into view pointing at the menu.
        float restPitch = -MathF.PI / 2f;
        bool menuOpen = IsLocalContextMenuOpen();

        var rightController = ResolveControllerSlot(ref _rightControllerSlot, Input.BodyNode.RightController);
        if (rightController != null)
        {
            float aimPitch = MathF.Min(MathF.Max(restPitch + _pitch, -MathF.PI / 2f), 0f);
            var targetRot = floatQ.Euler(-MathF.PI / 2f, aimPitch, 0f);
            var targetPos = menuOpen
                ? new float3(0.16f, headHeight - 0.28f, -0.28f)
                : new float3(0.25f, 1.0f, 0f);

            SetIfChanged(rightController.LocalPosition, targetPos);
            SetIfChanged(rightController.LocalRotation, targetRot);
        }

        var leftController = ResolveControllerSlot(ref _leftControllerSlot, Input.BodyNode.LeftController);
        if (leftController != null)
        {
            SetIfChanged(leftController.LocalPosition, new float3(-0.25f, 1.0f, 0f));
            SetIfChanged(leftController.LocalRotation, floatQ.Euler(MathF.PI / 2f, restPitch, 0f));
        }
    }

    private Slot? _leftControllerSlot;
    private Slot? _rightControllerSlot;
    private UI.ContextMenuSystem? _contextMenuCache;

    private Slot? ResolveControllerSlot(ref Slot? cache, Input.BodyNode node)
    {
        if (cache == null || cache.IsDestroyed)
            cache = _userRoot?.GetRegisteredComponent<TrackedDevicePositioner>(p => p.AutoBodyNode.Value == node)?.Slot;
        return cache;
    }

    private bool IsLocalContextMenuOpen()
    {
        if (_contextMenuCache == null || _contextMenuCache.IsDestroyed)
            _contextMenuCache = _userRoot?.Slot?.GetComponentInChildren<UI.ContextMenuSystem>();
        return _contextMenuCache?.IsOpen.Value == true;
    }

    private static void SetIfChanged(Sync<float3> field, in float3 value)
    {
        if ((field.Value - value).LengthSquared > POS_THRESHOLD_SQ)
            field.Value = value;
    }

    private static void SetIfChanged(Sync<floatQ> field, in floatQ value)
    {
        float dot = floatQ.Dot(field.Value, value);
        if (1.0f - (dot < 0 ? -dot : dot) > ROT_THRESHOLD)
            field.Value = value;
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

        SetRootRotation((floatQ.AxisAngle(float3.Up, deltaYaw) * Slot.GlobalRotation).Normalized);
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

        // Right as perpendicular on horizontal plane (Forward x Up = Right in right-handed system)
        right = float3.Cross(forward, float3.Up);
        if (right.LengthSquared < 1e-4f)
            right = float3.Right;
        right = right.Normalized;
    }

    public void ActivateModule(LocomotionModule module)
    {
        if (module == null || !_permissions.CanUseLocomotion(module))
            return;
        if (ReferenceEquals(module, _activeModule))
            return;

        _activeModule?.DeactivateInternal();
        _activeModule = module;
        _activeModule.ActivateInternal(this);
    }

    /// <summary>
    /// Expose CharacterController for modules.
    /// </summary>
    public CharacterController CharacterController => _characterController;

    /// <summary>
    /// Expose the controlled UserRoot for modules that move the rig directly
    /// (e.g. noclip flight bypasses the character controller).
    /// </summary>
    public UserRoot UserRoot => _userRoot;

    // CLEANUP

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

