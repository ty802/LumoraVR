using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Input.Drivers;

/// <summary>
/// Keyboard input driver - captures keyboard input.
/// </summary>
public partial class KeyboardDriver : Node
{
	public Vector2 MovementInput { get; private set; }
	public Vector2 CameraInput { get; private set; }
	public bool JumpPressed { get; private set; }
	public bool SprintHeld { get; private set; }
	public bool CrouchHeld { get; private set; }
	
	public override void _Ready()
	{
		AquaLogger.Log("KeyboardDriver initialized");
	}
	
	public override void _Process(double delta)
	{
		// WASD movement
		MovementInput = Godot.Input.GetVector("MoveLeft", "MoveRight", "MoveForward", "MoveBackward");
		
		// Arrow keys for camera
		CameraInput = Godot.Input.GetVector("CameraRight", "CameraLeft", "CameraDown", "CameraUp");
		
		// Action buttons
		JumpPressed = Godot.Input.IsActionPressed("Jump");
		SprintHeld = Godot.Input.IsActionPressed("Sprint");
		CrouchHeld = Godot.Input.IsActionPressed("Crouch");
	}
}
