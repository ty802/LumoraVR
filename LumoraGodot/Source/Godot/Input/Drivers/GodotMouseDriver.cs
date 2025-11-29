using Lumora.Core.Input;
using Lumora.Core.Math;
using Godot;

namespace Aquamarine.Source.Godot.Input.Drivers;

/// <summary>
/// Godot-specific mouse input driver.
/// Implements IMouseDriver and IInputDriver interfaces.
/// </summary>
public class GodotMouseDriver : IMouseDriver, IInputDriver
{
	public int UpdateOrder => 0;
	private Vector2 _lastMousePosition = Vector2.Zero;
	private float _lastScrollDelta = 0f;
	private double _lastFrameTime = 0;
	private Vector2 _accumulatedMotion = Vector2.Zero;

	public void UpdateMouse(Mouse mouse)
	{
		if (mouse == null)
			return;

		// Calculate delta time manually since we can't access Engine's delta
		double currentTime = Time.GetTicksMsec() / 1000.0;
		float deltaTime = _lastFrameTime > 0 ? (float)(currentTime - _lastFrameTime) : 0.016f;
		_lastFrameTime = currentTime;

		// Update button states
		mouse.LeftButton.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Left));
		mouse.RightButton.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Right));
		mouse.MiddleButton.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Middle));
		mouse.MouseButton4.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Xbutton1));
		mouse.MouseButton5.UpdateState(global::Godot.Input.IsMouseButtonPressed(MouseButton.Xbutton2));

		// Get mouse position using DisplayServer (Godot 4.x method)
		Vector2 mousePos = DisplayServer.MouseGetPosition();
		float2 position = new float2(mousePos.X, mousePos.Y);

		// Update positions
		mouse.DesktopPosition.UpdateValue(position, deltaTime);
		mouse.Position.UpdateValue(position, deltaTime);

		// Use accumulated motion from InputEventMouseMotion events (not position delta)
		// This works correctly when mouse is captured
		float2 delta = new float2(_accumulatedMotion.X, _accumulatedMotion.Y);
		// Commented out for less spam - uncomment for debugging
		// if (delta.LengthSquared > 0.001f)
		// {
		// 	GD.Print($"[GodotMouseDriver.UpdateMouse] Mouse delta: ({delta.x:F2}, {delta.y:F2})");
		// }
		mouse.DirectDelta.UpdateValue(delta, deltaTime);
		_accumulatedMotion = Vector2.Zero; // Reset for next frame

		// Update scroll wheel
		float scrollDelta = _lastScrollDelta;
		if (scrollDelta != 0)
		{
			GD.Print($"[GodotMouseDriver.UpdateMouse] Scroll delta: {scrollDelta}");
		}
		mouse.ScrollWheelDelta.UpdateValue(scrollDelta, deltaTime);
		_lastScrollDelta = 0f; // Reset for next frame
	}

	/// <summary>
	/// Called from Godot's _Input event to capture mouse motion and scroll wheel
	/// </summary>
	public void HandleInputEvent(InputEvent @event)
	{
		// Capture mouse motion (works correctly with captured mouse)
		if (@event is InputEventMouseMotion mouseMotion)
		{
			_accumulatedMotion += mouseMotion.Relative;
			// Commented out for less spam - uncomment for debugging
			// if (mouseMotion.Relative.Length() > 0.01f)
			// {
			// 	GD.Print($"[GodotMouseDriver.HandleInputEvent] Mouse motion: Relative({mouseMotion.Relative.X:F2}, {mouseMotion.Relative.Y:F2})");
			// }
		}
		// Capture scroll wheel
		else if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.WheelUp)
			{
				_lastScrollDelta += 1f;
				GD.Print("[GodotMouseDriver.HandleInputEvent] Mouse wheel up");
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
			{
				_lastScrollDelta -= 1f;
				GD.Print("[GodotMouseDriver.HandleInputEvent] Mouse wheel down");
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
