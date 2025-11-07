using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Input.Drivers;

/// <summary>
/// Mouse input driver - captures mouse movement and button presses.
/// </summary>
public partial class MouseDriver : Node
{
	private Window _window;
	private Vector2 _mouseDelta;
	private Vector2 _previousMouseDelta;
	
	public static float Sensitivity { get; set; } = 6f;
	
	public Vector2 MouseDelta => _mouseDelta;
	public Vector2 NormalizedMouseDelta => _mouseDelta * Mathf.Pi * Sensitivity;
	
	public override void _Ready()
	{
		_window = GetViewport().GetWindow();
		AquaLogger.Log("MouseDriver initialized");
	}
	
	public override void _Process(double delta)
	{
		// Clear previous frame's delta
		_mouseDelta -= _previousMouseDelta;
		_previousMouseDelta = _mouseDelta;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
		{
			// Normalize by screen height for consistent sensitivity
			_mouseDelta += -(motion.ScreenRelative / _window.Size.Y);
		}
	}
	
	public void ResetDelta()
	{
		_mouseDelta = Vector2.Zero;
		_previousMouseDelta = Vector2.Zero;
	}
}
