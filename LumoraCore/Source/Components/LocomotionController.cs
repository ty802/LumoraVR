using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Input;
using AquaLogger = Lumora.Core.Logging.Logger;
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

	public float MouseSensitivity { get; set; } = 0.003f;
	public float MaxPitch { get; set; } = 89.0f;

	// ===== STATE =====

	private CharacterController _characterController;
	private UserRoot _userRoot;
	private Mouse _mouse;
	private IKeyboardDriver _keyboardDriver;

	private float _pitch = 0.0f;
	private float _yaw = 0.0f;
	private bool _mouseCaptured = false;

	// ===== INITIALIZATION =====

	public override void OnAwake()
	{
		base.OnAwake();

		_userRoot = Slot.GetComponent<UserRoot>();
		if (_userRoot == null)
		{
			AquaLogger.Warn("LocomotionController: No UserRoot found!");
			return;
		}

		// Only work for local user
		if (_userRoot.ActiveUser != World.LocalUser)
			return;

		// Get CharacterController
		_characterController = Slot.GetComponent<CharacterController>();
		if (_characterController == null)
		{
			AquaLogger.Warn("LocomotionController: No CharacterController found!");
			return;
		}

		// Get input devices from InputInterface
		var inputInterface = Lumora.Core.Engine.Instance.InputInterface;
		if (inputInterface != null)
		{
			_mouse = inputInterface.Mouse;
			_keyboardDriver = inputInterface.GetKeyboardDriver();
		}

		// Capture mouse
		CaptureMouse();

		AquaLogger.Log($"LocomotionController: Initialized for local user '{_userRoot.ActiveUser.UserName.Value}'");
	}

	private void CaptureMouse()
	{
		_mouseCaptured = true;
		// Mouse capture is handled by platform-specific input manager
		// For Godot: InputManager.Instance.MovementLocked = false
		AquaLogger.Log("LocomotionController: Mouse captured for camera control");
	}

	// ===== UPDATE =====

	public override void OnUpdate(float delta)
	{
		base.OnUpdate(delta);

		if (_characterController == null || _userRoot?.ActiveUser != World.LocalUser)
			return;

		// Always handle mouse look (updates head rotation)
		HandleMouseLook(delta);

		// Only send movement/jump commands if CharacterController is ready
		if (_characterController.IsReady)
		{
			HandleMovement(delta);
			HandleJump();
		}

		UpdateHead();
	}

	private void HandleMouseLook(float delta)
	{
		// Check for Escape to toggle mouse capture
		if (_keyboardDriver != null && _keyboardDriver.GetKeyState(EngineKey.Escape))
		{
			_mouseCaptured = !_mouseCaptured;
			// Platform-specific mouse capture toggle handled by input manager
			// For Godot: InputManager.Instance.MovementLocked = !_mouseCaptured
			AquaLogger.Log($"LocomotionController: Mouse capture toggled: {_mouseCaptured}");
		}

		if (_mouse == null || !_mouseCaptured)
			return;

		// Get mouse delta from input driver
		var mouseDelta = _mouse.DirectDelta.Value;

		// Update yaw/pitch
		_yaw -= mouseDelta.x * MouseSensitivity;
		_pitch -= mouseDelta.y * MouseSensitivity;
		float maxPitchRad = MaxPitch * (float)System.Math.PI / 180f;
		_pitch = System.Math.Clamp(_pitch, -maxPitchRad, maxPitchRad);
	}

	private void HandleMovement(float delta)
	{
		if (_keyboardDriver == null || !_characterController.IsReady)
			return;

		// Get WASD input
		float2 inputDir = float2.Zero;
		if (_keyboardDriver.GetKeyState(EngineKey.W)) inputDir.y += 1;
		if (_keyboardDriver.GetKeyState(EngineKey.S)) inputDir.y -= 1;
		if (_keyboardDriver.GetKeyState(EngineKey.A)) inputDir.x -= 1;
		if (_keyboardDriver.GetKeyState(EngineKey.D)) inputDir.x += 1;
		inputDir = inputDir.Normalized;

		if (inputDir.LengthSquared > 0.001f)
		{
			// Calculate movement direction relative to yaw (no pitch)
			floatQ yawRotation = floatQ.AxisAngle(float3.Up, _yaw);
			float3 forward = yawRotation * float3.Forward;
			float3 right = yawRotation * float3.Right;

			// Project onto horizontal plane
			forward.y = 0;
			right.y = 0;
			forward = forward.Normalized;
			right = right.Normalized;

			// Calculate movement direction in world space
			float3 moveDir = (forward * inputDir.y + right * inputDir.x).Normalized;

			// Send to CharacterController
			_characterController.SetMovementDirection(moveDir);
		}
		else
		{
			_characterController.SetMovementDirection(float3.Zero);
		}
	}

	// TODO: Input driver system - Move to input hook
	private void HandleJump()
	{
		if (_keyboardDriver == null)
			return;

		// Space to jump
		if (_keyboardDriver.GetKeyState(EngineKey.Space))
		{
			_characterController.RequestJump();
		}
	}

	// TODO: Physics driver system - Move to physics hook
	private void UpdateHead()
	{
		if (_userRoot?.HeadSlot == null)
			return;

		// TODO: Physics driver - Update head rotation with pitch + yaw
		// var headRotation = floatQ.FromEuler(new float3(_pitch, _yaw, 0));
		// _userRoot.HeadSlot.GlobalTransform = new Transform(
		// 	new Basis(headRotation),
		// 	_userRoot.HeadSlot.GlobalPosition
		// );
		//
		// // Update slot rotation (yaw only, no pitch)
		// Slot.GlobalRotation = new float3(0, _yaw, 0);

		// Simplified - just set rotation directly
		_userRoot.HeadSlot.GlobalRotation = floatQ.FromEuler(new float3(_pitch, _yaw, 0));
		Slot.GlobalRotation = floatQ.FromEuler(new float3(0, _yaw, 0));
	}

	// ===== CLEANUP =====

	public override void OnDestroy()
	{
		// TODO: Input driver - Release mouse
		// if (_mouseCaptured)
		// {
		// 	InputSystem.MouseMode = MouseModeEnum.Visible;
		// }

		base.OnDestroy();
		AquaLogger.Log("LocomotionController: Destroyed");
	}
}
